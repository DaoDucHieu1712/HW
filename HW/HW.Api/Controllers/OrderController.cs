using HW.Api.Models;
using HW.Application.Features.Orders.Commands.PlaceOrder;
using HW.Application.Features.Orders.Queries.GetOrderSaga;
using MediatR;
using Microsoft.AspNetCore.Mvc;
using static HW.Application.Features.Orders.Dtos.OrderDtos;

namespace HW.Api.Controllers;

[Route("api/order")]
[ApiController]
public class OrderController : ControllerBase
{
    private readonly ISender _sender;

    public OrderController(ISender sender) => _sender = sender;

    /// <summary>
    /// Starts an order saga and returns immediately with the order id.
    /// </summary>
    /// <remarks>
    /// <b>202, not 201.</b> Nothing is created yet: the saga has one committed event and one queued
    /// command, and whether the order survives depends on services this request never spoke to. A
    /// 201 would promise a resource that may be cancelled seconds later. Poll
    /// <c>GET /api/order/{id}</c> until <c>status</c> leaves <c>Running</c>, or subscribe to the
    /// <c>order.saga.completed</c> / <c>order.saga.cancelled</c> topics.
    ///
    /// <para>
    /// With the outbox on (the default), the first command reaches the broker on the processor's
    /// next poll — up to ~10s — so a status read immediately after this call will still say
    /// <c>AwaitingStockReservation</c>. That lag is the visible price of the atomicity, not a bug.
    /// </para>
    ///
    /// <para>
    /// The stub participants make the outcome deterministic: quantity &gt; 10 is rejected by stock
    /// (saga ends <c>Compensated</c> with nothing to undo), amount &gt; 1000 is declined by payment
    /// after stock was already reserved (saga compensates by releasing it), and anything else
    /// completes.
    /// </para>
    /// </remarks>
    [HttpPost]
    [ProducesResponseType(StatusCodes.Status202Accepted)]
    public async Task<IActionResult> PlaceOrder([FromBody] PlaceOrderRequestDto dto)
    {
        var result = await _sender.Send(new PlaceOrderCommand(
            dto.CustomerId!, dto.Sku!, dto.Quantity, dto.Amount));

        return Accepted(ApiResponseFactory.Success(result));
    }

    /// <summary>Current state of an order saga.</summary>
    /// <param name="id">Order id returned by <c>POST /api/order</c>.</param>
    /// <param name="includeHistory">
    /// Include the full event stream. This is the event-sourcing payoff: every decision the
    /// orchestrator made, in order, with the data it made them from — the audit trail a distributed
    /// transaction is always eventually asked for.
    /// </param>
    [HttpGet("{id}")]
    public async Task<IActionResult> GetSaga(string id, [FromQuery] bool includeHistory = false)
    {
        var result = await _sender.Send(new GetOrderSagaQuery(id, includeHistory));
        return Ok(ApiResponseFactory.Success(result));
    }
}
