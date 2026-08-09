using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RowadUmrahSystem.Web.Data;
using RowadUmrahSystem.Web.Models.Accounting;
using RowadUmrahSystem.Web.Services;
using RowadUmrahSystem.Web.ViewModels.Api;

namespace RowadUmrahSystem.Web.Controllers.Api
{
    [ApiController]
    [Authorize]
    [Route("api/customers")]
    public class CustomersApiController : ControllerBase
    {
        private readonly ApplicationDbContext _context;
        private readonly PermissionService _permissionService;

        public CustomersApiController(
            ApplicationDbContext context,
            PermissionService permissionService)
        {
            _context = context;
            _permissionService = permissionService;
        }

        [HttpGet]
        public async Task<ActionResult<IReadOnlyList<CustomerListItemDto>>> GetAll(
            [FromQuery] string? search = null,
            [FromQuery] bool? isActive = null)
        {
            if (!await CanViewCustomers())
                return Forbid();

            var query = _context.Customers
                .AsNoTracking()
                .Include(x => x.Traveler)
                .AsQueryable();

            if (isActive.HasValue)
            {
                query = query.Where(x => x.IsActive == isActive.Value);
            }

            if (!string.IsNullOrWhiteSpace(search))
            {
                query = query.Where(x =>
                    x.Name.Contains(search) ||
                    x.CivilId.Contains(search) ||
                    x.PassportNumber.Contains(search) ||
                    x.PhoneNumber.Contains(search) ||
                    x.Email.Contains(search) ||
                    x.Address.Contains(search) ||
                    (x.Traveler != null && x.Traveler.FullName.Contains(search)));
            }

            var customers = await query
                .OrderByDescending(x => x.CreatedAt)
                .Select(x => new CustomerListItemDto(
                    x.Id,
                    x.Name,
                    x.CivilId,
                    x.PassportNumber,
                    x.PhoneNumber,
                    x.Email,
                    x.Address,
                    x.TravelerId,
                    x.Traveler != null ? x.Traveler.FullName : null,
                    x.IsActive,
                    x.CreatedAt))
                .ToListAsync();

            return Ok(customers);
        }

        [HttpGet("{id:int}")]
        public async Task<ActionResult<CustomerDetailDto>> GetById(int id)
        {
            if (!await CanViewCustomers())
                return Forbid();

            var customer = await _context.Customers
                .AsNoTracking()
                .Include(x => x.Traveler)
                .FirstOrDefaultAsync(x => x.Id == id);

            if (customer == null)
                return NotFound();

            return Ok(Map(customer));
        }

        [HttpPost]
        public async Task<ActionResult<CustomerDetailDto>> Create([FromBody] CustomerUpsertRequestDto request)
        {
            if (!await CanManageCustomers())
                return Forbid();

            var validation = await ValidateAsync(request);
            if (validation != null)
                return validation;

            var customer = new Customer
            {
                Name = request.Name.Trim(),
                CivilId = request.CivilId.Trim(),
                PassportNumber = request.PassportNumber.Trim(),
                PhoneNumber = request.PhoneNumber.Trim(),
                Email = request.Email.Trim(),
                Address = request.Address.Trim(),
                TravelerId = request.TravelerId,
                IsActive = request.IsActive,
                CreatedAt = DateTime.Now
            };

            _context.Customers.Add(customer);
            await _context.SaveChangesAsync();

            return Ok(await MapAsync(customer.Id));
        }

        [HttpPut("{id:int}")]
        public async Task<ActionResult<CustomerDetailDto>> Update(int id, [FromBody] CustomerUpsertRequestDto request)
        {
            if (!await CanManageCustomers())
                return Forbid();

            var customer = await _context.Customers.FirstOrDefaultAsync(x => x.Id == id);
            if (customer == null)
                return NotFound();

            var validation = await ValidateAsync(request);
            if (validation != null)
                return validation;

            customer.Name = request.Name.Trim();
            customer.CivilId = request.CivilId.Trim();
            customer.PassportNumber = request.PassportNumber.Trim();
            customer.PhoneNumber = request.PhoneNumber.Trim();
            customer.Email = request.Email.Trim();
            customer.Address = request.Address.Trim();
            customer.TravelerId = request.TravelerId;
            customer.IsActive = request.IsActive;

            await _context.SaveChangesAsync();

            return Ok(await MapAsync(customer.Id));
        }

        [HttpPost("{id:int}/toggle-status")]
        public async Task<ActionResult<CustomerDetailDto>> ToggleStatus(int id)
        {
            if (!await CanManageCustomers())
                return Forbid();

            var customer = await _context.Customers.FirstOrDefaultAsync(x => x.Id == id);
            if (customer == null)
                return NotFound();

            customer.IsActive = !customer.IsActive;
            await _context.SaveChangesAsync();

            return Ok(await MapAsync(customer.Id));
        }

        private async Task<ActionResult?> ValidateAsync(CustomerUpsertRequestDto request)
        {
            var name = request.Name?.Trim() ?? string.Empty;
            var civilId = request.CivilId?.Trim() ?? string.Empty;
            var passportNumber = request.PassportNumber?.Trim() ?? string.Empty;
            var phoneNumber = request.PhoneNumber?.Trim() ?? string.Empty;
            var email = request.Email?.Trim() ?? string.Empty;
            var address = request.Address?.Trim() ?? string.Empty;

            if (string.IsNullOrWhiteSpace(name))
                return BadRequest("Customer name is required.");

            if (string.IsNullOrWhiteSpace(civilId))
                return BadRequest("Civil ID is required.");

            if (string.IsNullOrWhiteSpace(passportNumber))
                return BadRequest("Passport number is required.");

            if (string.IsNullOrWhiteSpace(phoneNumber))
                return BadRequest("Phone number is required.");

            if (string.IsNullOrWhiteSpace(email))
                return BadRequest("Email is required.");

            if (string.IsNullOrWhiteSpace(address))
                return BadRequest("Address is required.");

            if (request.TravelerId.HasValue)
            {
                var travelerExists = await _context.Travelers
                    .AnyAsync(x => x.Id == request.TravelerId.Value && !x.IsDeleted);

                if (!travelerExists)
                    return BadRequest("Traveler not found.");
            }

            return null;
        }

        private async Task<CustomerDetailDto> MapAsync(int customerId)
        {
            var customer = await _context.Customers
                .AsNoTracking()
                .Include(x => x.Traveler)
                .FirstAsync(x => x.Id == customerId);

            return Map(customer);
        }

        private static CustomerDetailDto Map(Customer customer)
        {
            return new CustomerDetailDto(
                customer.Id,
                customer.Name,
                customer.CivilId,
                customer.PassportNumber,
                customer.PhoneNumber,
                customer.Email,
                customer.Address,
                customer.TravelerId,
                customer.Traveler?.FullName,
                customer.IsActive,
                customer.CreatedAt);
        }

        private async Task<bool> CanViewCustomers()
        {
            return await _permissionService.HasPermissionAsync(User, "Accounting.View") ||
                   await _permissionService.HasPermissionAsync(User, "Accounting.Manage");
        }

        private async Task<bool> CanManageCustomers()
        {
            return await _permissionService.HasPermissionAsync(User, "Accounting.Manage");
        }
    }
}
