using MediatR;

namespace HW.Application.CQRS;

public interface IBaseCommand { }

public interface ICommand : IRequest<Unit>, IBaseCommand { }
public interface ICommand<TResponse> : IRequest<TResponse>, IBaseCommand { }
