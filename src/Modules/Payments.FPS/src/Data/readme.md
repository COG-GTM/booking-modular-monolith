 dotnet ef migrations add initial --context PaymentsDbContext -o Data/Migrations
 dotnet ef database update --context PaymentsDbContext
