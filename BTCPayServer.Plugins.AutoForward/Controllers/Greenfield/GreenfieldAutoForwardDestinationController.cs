using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BTCPayServer.Abstractions.Constants;
using BTCPayServer.Abstractions.Custodians;
using BTCPayServer.Client;
using BTCPayServer.Client.Models;
using BTCPayServer.Plugins.AutoForward.Data;
using BTCPayServer.Plugins.AutoForward.Data.Client;
using BTCPayServer.Plugins.AutoForward.Data.Client.Request;
using BTCPayServer.Plugins.AutoForward.Exception;
using BTCPayServer.Plugins.AutoForward.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Cors;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace BTCPayServer.Plugins.AutoForward.Controllers.Greenfield
{
    public class AutoForwardApiException : System.Exception
    {
        public int HttpStatus { get; }
        public string Code { get; }

        private AutoForwardApiException(int httpStatus, string code, string message, System.Exception ex) : base(
            message, ex)
        {
            HttpStatus = httpStatus;
            Code = code;
        }

        public AutoForwardApiException(int httpStatus, string code, string message) : this(httpStatus, code, message,
            null)
        {
        }
    }

    public class AutoForwardExceptionFilter : Attribute, IExceptionFilter
    {
        public void OnException(ExceptionContext context)
        {
            if (context.Exception is AutoForwardApiException ex)
            {
                context.Result = new ObjectResult(new GreenfieldAPIError(ex.Code, ex.Message))
                    { StatusCode = ex.HttpStatus };
                context.ExceptionHandled = true;
            }
        }
    }


    [ApiController]
    [Authorize(AuthenticationSchemes = AuthenticationSchemes.GreenfieldAPIKeys)]
    [EnableCors(CorsPolicies.All)]
    [AutoForwardExceptionFilter]
    public class GreenfieldAutoForwardDestinationController : ControllerBase
    {
        private readonly AutoForwardDestinationRepository _autoForwardDestinationRepository;
        private readonly AutoForwardInvoiceHelper _helper;

        public GreenfieldAutoForwardDestinationController(
            AutoForwardDestinationRepository autoForwardDestinationRepository, AutoForwardInvoiceHelper helper)
        {
            _autoForwardDestinationRepository = autoForwardDestinationRepository;
            _helper = helper;
        }


        [HttpGet("~/api/v1/stores/{storeId}/autoforward-destinations")]
        [Authorize(Policy = Policies.CanManagePullPayments,
            AuthenticationSchemes = AuthenticationSchemes.Greenfield)]
        public async Task<IActionResult> ListAutoForwardDestination(string storeId,
            CancellationToken cancellationToken = default)
        {
            AutoForwardDestination[] destinations =
                await _autoForwardDestinationRepository.FindByStoreId(storeId, cancellationToken);
            AutoForwardDestinationData[] responses = new AutoForwardDestinationData[destinations.Length];

            for (int i = 0; i < destinations.Length; i++)
            {
                var destination = destinations[i];
                responses[i] = ToModel(destination);
            }

            return Ok(responses);
        }


        [HttpPost("~/api/v1/stores/{storeId}/autoforward-destinations")]
        [Authorize(Policy = Policies.CanManagePullPayments,
            AuthenticationSchemes = AuthenticationSchemes.Greenfield)]
        public async Task<IActionResult> CreateAutoForwardDestination(string storeId,
            CreateAutoForwardDestinationRequest request, CancellationToken cancellationToken)
        {
            request ??= new CreateAutoForwardDestinationRequest();

            AutoForwardDestination entity = new AutoForwardDestination
            {
                Destination = request.Destination,
                StoreId = storeId,
                PaymentMethod = request.PaymentMethod,
                Balance = 0,
                PayoutsAllowed = false
            };

            try
            {
                entity = await _autoForwardDestinationRepository.Create(entity);
                await _helper.UpdatePayoutToDestination(entity, null, null, cancellationToken);
            }
            catch (DbUpdateException e)
            {
                if (e.InnerException != null && e.InnerException.Message.Contains("duplicate key value"))
                {
                    throw new AutoForwardApiException(409, "destination-already-exists",
                        $"Destination {request.Destination} already exists in store {storeId}.");
                }

                throw;
            }

            return Ok(ToModel(entity));
        }

        [HttpPut("~/api/v1/stores/{storeId}/autoforward-destinations/{destinationId}")]
        [Authorize(Policy = Policies.CanManagePullPayments,
            AuthenticationSchemes = AuthenticationSchemes.Greenfield)]
        public async Task<IActionResult> UpdateAutoForwardDestination(string storeId, string destinationId,
            UpdateAutoForwardDestinationRequest request, CancellationToken cancellationToken)
        {
            request ??= new UpdateAutoForwardDestinationRequest();
            if (request.PayoutsAllowed == null && request.Destination == null)
            {
                throw new AutoForwardApiException(400, "missing-data",
                    $"A value for 'PayoutsAllowed' or 'Destination' must be provided.");
            }

            try
            {
                AutoForwardDestination entity =
                    await _autoForwardDestinationRepository.FindById(storeId, destinationId);

                if (entity == null)
                {
                    throw new RecordNotFoundException();
                }

                string oldDestination = entity.Destination;
                bool oldAllowed = entity.PayoutsAllowed;

                if (request.Destination != null)
                {
                    if (!entity.Destination.Equals(request.Destination, StringComparison.InvariantCulture))
                    {
                        // Destination change, disable payouts again... unless it should be activated which comes next.
                        entity.Destination = request.Destination;
                        request.PayoutsAllowed = false;
                    }
                }

                if (request.PayoutsAllowed != null)
                {
                    entity.PayoutsAllowed = (bool)request.PayoutsAllowed;
                }

                entity = await _autoForwardDestinationRepository.Update(entity);
                await _helper.UpdatePayoutToDestination(entity, oldDestination, oldAllowed, cancellationToken);

                return Ok(ToModel(entity));
            }
            catch (RecordNotFoundException)
            {
                throw new AutoForwardApiException(404, "destination-not-found",
                    $"Destination {destinationId} could not be found in store {storeId}.");
            }
        }

        private AutoForwardDestinationData ToModel(AutoForwardDestination destination)
        {
            AutoForwardDestinationData r = new()
            {
                Id = destination.Id,
                Destination = destination.Destination,
                StoreId = destination.StoreId,
                PayoutsAllowed = destination.PayoutsAllowed,
                PaymentMethod = destination.PaymentMethod
            };
            return r;
        }
    }
}