using System.Text.Json;
using HW.Application.Abstractions.Caching;
using HW.Application.Abstractions.Messaging;
using HW.Domain.Abstractions;
using HW.Domain.Abstractions.Repositories;
using HW.Domain.Abstractions.Sagas;
using HW.Infrastructure.Sagas;
using HW.Domain.Entities;
using HW.Infrastructure.Caching;
using HW.Infrastructure.Diagnostics;
using HW.Infrastructure.Interceptors;
using HW.Infrastructure.Messaging;
using HW.Infrastructure.Messaging.Kafka;
using HW.Infrastructure.Messaging.MassTransitAdapter;
using HW.Infrastructure.Messaging.RabbitMq;
using HW.Infrastructure.Repositories;
using MassTransit;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using StackExchange.Redis;
using static HW.Infrastructure.DI.Options;

namespace HW.Infrastructure.DI;

public static class ServiceCollectionExtensions
{
    public static void AddMariaDbConfiguration(this IServiceCollection services, IConfiguration configuration, bool isDevelopment)
    {
        services.AddDbContext<ApplicationDbContext>((provider, builder) =>
        {
            var connectionString = configuration.GetConnectionString("DefaultConnection");
            var auditableInterceptor = provider.GetService<AuditableEntitiesInterceptor>();
            var options = provider.GetRequiredService<IOptionsMonitor<MariaDbRetryOptions>>();

            // The SQL trace interceptor is optional: it is registered with the agent stack, and the
            // app has to run without it. Both are collected here so the null-tolerant call below
            // takes whatever is present.
            IInterceptor?[] interceptors =
            [
                auditableInterceptor,
                provider.GetService<SqlTraceInterceptor>(),
            ];

            builder
               .EnableDetailedErrors(isDevelopment)
               .EnableSensitiveDataLogging(isDevelopment)
               .UseLazyLoadingProxies(true)
               .UseMySql(
                    connectionString: connectionString,
                    ServerVersion.AutoDetect(connectionString),
                    mySqlOptionsAction: optionsBuilder
                        => optionsBuilder.ExecutionStrategy(
                                dependencies => new MySqlRetryingExecutionStrategy(
                                    dependencies: dependencies,
                                    maxRetryCount: options.CurrentValue.MaxRetryCount,
                                    maxRetryDelay: options.CurrentValue.MaxRetryDelay,
                                    errorNumbersToAdd: options.CurrentValue.ErrorNumbersToAdd))
                            .MigrationsAssembly(typeof(ApplicationDbContext).Assembly.GetName().Name))
               .AddInterceptors(interceptors.OfType<IInterceptor>().ToArray());

        }).AddIdentity<AppUser, AppRole>()
            .AddEntityFrameworkStores<ApplicationDbContext>()
            .AddDefaultTokenProviders();

        services.Configure<IdentityOptions>(options =>
        {
            options.Password.RequireDigit = false;
            options.Password.RequireLowercase = false;
            options.Password.RequireNonAlphanumeric = false;
            options.Password.RequireUppercase = false;
            options.Password.RequiredLength = 3;
            options.Password.RequiredUniqueChars = 1;

            options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(5);
            options.Lockout.MaxFailedAccessAttempts = 5;
            options.Lockout.AllowedForNewUsers = true;

            options.User.AllowedUserNameCharacters =
                "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789-._@+";
            options.User.RequireUniqueEmail = true;

            options.SignIn.RequireConfirmedEmail = true;
            options.SignIn.RequireConfirmedPhoneNumber = false;
        });
    }

    public static void AddInfrastructureServices(this IServiceCollection services)
    {
        services.AddHttpContextAccessor();
        services.AddScoped(typeof(IRepository<>), typeof(EFRepository<>));
        services.AddScoped<IUnitOfWork, EFUnitOfWork>();
        services.AddScoped<ISagaRepository, SagaRepository>();
        services.AddHostedService<Outbox.OutboxMessageProcessor>();
    }

    public static void AddInterceptorDbContext(this IServiceCollection services)
    {
        services.AddSingleton<AuditableEntitiesInterceptor>();
    }

    /// <summary>
    /// Registers the two-tier cache: in-process memory (L1) in front of Redis (L2).
    /// With no <c>Cache:RedisConnection</c> configured the L2 tier is skipped entirely and the
    /// service runs L1-only — useful for local dev and tests without a Redis container.
    /// </summary>
    public static void AddCaching(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<CacheOptions>()
            .Bind(configuration.GetSection("Cache"))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddMemoryCache();

        var connectionString = configuration.GetSection("Cache")["RedisConnection"];
        if (!string.IsNullOrWhiteSpace(connectionString))
        {
            services.AddSingleton<IConnectionMultiplexer>(provider =>
            {
                var config = ConfigurationOptions.Parse(connectionString);

                // Never let a cold or missing Redis take the app down at startup: connect in the
                // background and let TwoTierCacheService degrade to L1-only until it comes up.
                config.AbortOnConnectFail = false;

                return ConnectionMultiplexer.Connect(config);
            });

            // Build the multiplexer at boot instead of on the first request to touch the cache.
            services.AddHostedService<RedisConnectionWarmup>();
        }

        services.AddSingleton<ICacheService, TwoTierCacheService>();
    }

    /// <summary>
    /// Wires up the message bus adapter named by <c>Messaging:Provider</c> — RabbitMQ, Kafka, or a
    /// no-op when the section is absent. Handlers are registered separately with
    /// <see cref="AddMessageHandler{TMessage, THandler}"/>, in either order.
    ///
    /// <para>
    /// Only the selected provider's client is constructed, so an unreachable RabbitMQ costs nothing
    /// in a Kafka deployment and neither is touched under <c>None</c>.
    /// </para>
    /// </summary>
    public static void AddMessaging(this IServiceCollection services, IConfiguration configuration)
    {
        var section = configuration.GetSection("Messaging");

        services.AddOptions<MessagingOptions>()
            .Bind(section)
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddSingleton<MessageDispatcher>();
        GetSubscriptionRegistry(services);

        switch (section.GetValue<MessageBrokerProvider>("Provider"))
        {
            case MessageBrokerProvider.RabbitMq:
                services.AddSingleton<RabbitMqConnectionProvider>();
                services.AddSingleton<RabbitMqMessageBus>();

                // The concrete type is registered too, and IBrokerBus forwards to it: the consumer
                // needs the adapter's internal envelope-publishing path for dead-lettering, which is
                // deliberately not on the IMessageBus contract.
                services.AddSingleton<IBrokerBus>(sp => sp.GetRequiredService<RabbitMqMessageBus>());
                services.AddHostedService<RabbitMqConsumerService>();
                break;

            case MessageBrokerProvider.Kafka:
                services.AddSingleton<KafkaMessageBus>();
                services.AddSingleton<IBrokerBus>(sp => sp.GetRequiredService<KafkaMessageBus>());
                services.AddHostedService<KafkaConsumerService>();
                break;

            case MessageBrokerProvider.MassTransit:
                AddMassTransitProvider(services, section);
                break;

            case MessageBrokerProvider.None:
            default:
                services.AddSingleton<IBrokerBus, NullMessageBus>();
                break;
        }

        AddMessageBusFacade(services, section);
    }

    /// <summary>
    /// Decides what <see cref="IMessageBus"/> means to callers: the outbox, or the broker directly.
    ///
    /// <para>
    /// Registered scoped either way. The outbox bus needs the request's <see cref="ApplicationDbContext"/>
    /// to enlist in its transaction, and resolving the direct path through a scope too keeps the
    /// lifetime identical whichever way the flag is set — so a captive-dependency bug cannot appear
    /// only in the configuration nobody tested.
    /// </para>
    /// </summary>
    private static void AddMessageBusFacade(IServiceCollection services, IConfigurationSection section)
    {
        if (section.GetValue("UseOutbox", true))
            services.AddScoped<IMessageBus, OutboxMessageBus>();
        else
            services.AddScoped<IMessageBus>(sp => sp.GetRequiredService<IBrokerBus>());
    }

    /// <summary>
    /// Wires MassTransit over RabbitMQ behind <see cref="IMessageBus"/>, reusing the
    /// <c>Messaging:RabbitMq</c> connection settings.
    ///
    /// <para>
    /// MassTransit brings its own retry, error queues, and hosted bus, so neither
    /// <c>MessageDispatcher</c> nor the hand-rolled consumer service is registered on this path.
    /// Its retry is configured from the same options instead, so the policy stays the same across
    /// providers even though the machinery differs.
    /// </para>
    /// </summary>
    private static void AddMassTransitProvider(IServiceCollection services, IConfigurationSection section)
    {
        var options = section.Get<MessagingOptions>() ?? new MessagingOptions();
        var registry = GetSubscriptionRegistry(services);

        // Consumers must be in the container before AddMassTransit returns, so no handler may be
        // registered after this point. Seal now for a clear error rather than a silent no-op.
        registry.Seal();

        services.AddMassTransit(bus =>
        {
            foreach (var subscription in registry.Subscriptions)
                bus.AddConsumer(ConsumerTypeFor(subscription));

            bus.UsingRabbitMq((context, cfg) =>
            {
                ApplyLicense(cfg, options.MassTransit);

                cfg.Host(options.RabbitMq.HostName, (ushort)options.RabbitMq.Port, options.RabbitMq.VirtualHost, host =>
                {
                    host.Username(options.RabbitMq.UserName);
                    host.Password(options.RabbitMq.Password);
                });

                // Publish to exchanges named by [Message], not by CLR type. The existing formatter is
                // kept as the fallback for MassTransit's own internal contracts.
                cfg.MessageTopology.SetEntityNameFormatter(
                    new MessageTopicEntityNameFormatter(cfg.MessageTopology.EntityNameFormatter));

                foreach (var subscription in registry.Subscriptions)
                {
                    // Same queue naming as the hand-rolled adapter: competing consumers across every
                    // instance sharing the group.
                    cfg.ReceiveEndpoint($"{options.ConsumerGroup}.{subscription.Topic}", endpoint =>
                    {
                        endpoint.PrefetchCount = options.RabbitMq.PrefetchCount;

                        // Failures beyond this go to MassTransit's {queue}_error queue — its
                        // dead-letter, which is NOT the {topic}.dlq the other providers use.
                        endpoint.UseMessageRetry(retry => ConfigureRetry(retry, options));

                        endpoint.ConfigureConsumer(context, ConsumerTypeFor(subscription));
                    });
                }
            });
        });

        // Scoped, not singleton: MassTransit registers IPublishEndpoint per scope, so a singleton
        // here would capture the first scope's endpoint and hold it for the life of the process.
        services.AddScoped<IBrokerBus, MassTransitMessageBus>();
    }

    private static Type ConsumerTypeFor(MessageSubscription subscription)
        => typeof(MessageHandlerConsumer<>).MakeGenericType(subscription.MessageType);

    /// <summary>
    /// Reproduces <see cref="MessageDispatcher"/>'s schedule on MassTransit's retry filter, so a
    /// handler sees the same number of attempts with the same backoff whichever provider is running.
    /// MassTransit counts <i>retries</i> where the dispatcher counts <i>attempts</i>, hence the -1.
    /// </summary>
    private static void ConfigureRetry(IRetryConfigurator retry, MessagingOptions options)
    {
        var retryCount = options.MaxDeliveryAttempts - 1;

        if (retryCount <= 0)
        {
            retry.None();
            return;
        }

        retry.Intervals(Enumerable
            .Range(0, retryCount)
            .Select(i => TimeSpan.FromMilliseconds(options.RetryBaseDelayMs * Math.Pow(2, i)))
            .ToArray());
    }

    /// <summary>
    /// MassTransit v9 is commercial and validates a licence when the bus is configured. Configuring
    /// nothing is a legitimate choice — MassTransit reads <c>MT_LICENSE</c> / <c>MT_LICENSE_PATH</c>
    /// from the environment on its own, which keeps the key out of appsettings.json.
    /// </summary>
    private static void ApplyLicense(IBusFactoryConfigurator cfg, MassTransitOptions options)
    {
        if (!string.IsNullOrWhiteSpace(options.LicenseKey))
            cfg.SetLicense(options.LicenseKey);
        else if (!string.IsNullOrWhiteSpace(options.LicensePath))
            cfg.SetLicenseLocation(options.LicensePath);
    }

    /// <summary>
    /// Registers <typeparamref name="THandler"/> for <typeparamref name="TMessage"/> and subscribes
    /// the consumer to that message's <c>[Message]</c> topic.
    ///
    /// <para>
    /// Handlers are scoped — each message is dispatched inside its own scope — so they may inject
    /// repositories and anything else with a per-request lifetime.
    /// </para>
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// <typeparamref name="TMessage"/> has no <c>[Message]</c> topic, or another handler already
    /// claims that topic. Both throw here, at startup, rather than on the first delivery.
    /// </exception>
    public static IServiceCollection AddMessageHandler<TMessage, THandler>(this IServiceCollection services)
        where TMessage : class
        where THandler : class, IMessageHandler<TMessage>
    {
        services.AddScoped<IMessageHandler<TMessage>, THandler>();

        GetSubscriptionRegistry(services).Add(new MessageSubscription(
            Topic: MessageSerializer.TopicOf<TMessage>(),
            MessageType: typeof(TMessage),
            HandlerType: typeof(THandler),

            // Closed over TMessage while it is still a static type argument, so dispatch costs no
            // reflection per message.
            Invoke: static async (provider, payload, context, ct) =>
            {
                var message = payload.Deserialize<TMessage>(MessageSerializer.Options)
                    ?? throw new InvalidOperationException(
                        $"A '{context.Topic}' payload deserialized to null as {typeof(TMessage).Name}.");

                await provider.GetRequiredService<IMessageHandler<TMessage>>()
                    .HandleAsync(message, context, ct);
            }));

        return services;
    }

    /// <summary>
    /// The registry is built during registration rather than resolved from the container, because
    /// <see cref="AddMessageHandler{TMessage, THandler}"/> has to write to it before any provider
    /// exists. Registering the instance keeps it the same object the consumer later reads.
    /// </summary>
    private static MessageSubscriptionRegistry GetSubscriptionRegistry(IServiceCollection services)
    {
        var registered = services
            .FirstOrDefault(d => d.ServiceType == typeof(MessageSubscriptionRegistry))?
            .ImplementationInstance;

        if (registered is MessageSubscriptionRegistry existing) return existing;

        var registry = new MessageSubscriptionRegistry();
        services.AddSingleton(registry);

        return registry;
    }

    public static OptionsBuilder<MariaDbRetryOptions> ConfigureMariaDbRetryOptions(this IServiceCollection services, IConfigurationSection section)
        => services
            .AddOptions<MariaDbRetryOptions>()
            .Bind(section)
            .ValidateDataAnnotations()
            .ValidateOnStart();
}
