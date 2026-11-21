using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using SchoolInventoryManagement.DAL.Context;
using SchoolInventoryManagement.DAL.Entities;

namespace SchoolInventoryManagement.BLL.Services
{
    // Where things for a department go (Scripts/029): its main room, else
    // the first of its rooms. Requests no longer ask where to deliver; this
    // is taken from the requester's department.
    public static class DepartmentRooms
    {
        public static async Task<Location> DeliveryRoomAsync(ApplicationDbContext context, int departmentId)
        {
            var department = await context.Departments.AsNoTracking()
                .Where(d => d.DepartmentID == departmentId)
                .Select(d => new { d.DepartmentName, d.MainLocationID })
                .FirstOrDefaultAsync()
                ?? throw new InvalidOperationException("Your account has no department. Ask an administrator to set it.");

            var room = await context.Locations
                .Where(l => l.DepartmentID == departmentId)
                .OrderBy(l => l.LocationID == department.MainLocationID ? 0 : 1).ThenBy(l => l.LocationName)
                .FirstOrDefaultAsync();
            return room ?? throw new InvalidOperationException(
                $"The {department.DepartmentName} has no room yet, so there is nowhere to deliver to. " +
                "Ask an administrator to give it a room on the Departments page.");
        }

        // The requester's department room, for showing on a request form;
        // null when it has none.
        public static async Task<string?> DeliveryRoomNameAsync(ApplicationDbContext context, int userId)
        {
            var departmentId = await context.Users.Where(u => u.UserID == userId).Select(u => u.DepartmentID).FirstOrDefaultAsync();
            try
            {
                return (await DeliveryRoomAsync(context, departmentId)).LocationName.Trim();
            }
            catch (InvalidOperationException)
            {
                return null;
            }
        }
    }
}
