using MediatR;

namespace HW.Application.CQRS;

public interface IQuery<TResponse> : IRequest<TResponse> { }
