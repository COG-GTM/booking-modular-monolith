namespace Passenger.Host.Test;

public static class TestWait
{
    public static async Task<T?> UntilAsync<T>(Func<Task<T?>> probe, TimeSpan timeout)
        where T : class
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            var result = await probe();
            if (result is not null)
            {
                return result;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(500));
        }

        return null;
    }
}
