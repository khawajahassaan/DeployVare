using API.Models.Entities;
using API.Models.Enums;
using Microsoft.EntityFrameworkCore;

namespace API.Data;

public static class DbSeeder
{
    public static async Task SeedAsync(DeployVareDbContext context)
    {
        // Ensure database is created
        await context.Database.EnsureCreatedAsync();

        // Check if admin user already exists
        if (await context.Users.AnyAsync(u => u.Username == "admin"))
        {
            Console.WriteLine("✅ Admin user already exists");
            return;
        }

        // Create default admin user
        var adminUser = new User
        {
            Username = "admin",
            // Password: Admin@123
            PasswordHash = BCrypt.Net.BCrypt.HashPassword("Admin@123"),
            FullName = "System Administrator",
            Role = UserRole.Admin,
            CreatedAt = DateTime.UtcNow
        };

        context.Users.Add(adminUser);
        await context.SaveChangesAsync();

        Console.WriteLine("✅ Admin user created successfully!");
        Console.WriteLine("   Username: admin");
        Console.WriteLine("   Password: Admin@123");
    }
}

