namespace BuildingBlocks.Grpc;

public class GrpcClientOptions
{
    // Logical service URI resolved through service discovery, e.g. "https://flight".
    public string Address { get; set; } = default!;
    public TimeSpan Deadline { get; set; } = TimeSpan.FromSeconds(10);
    public int MaxAttempts { get; set; } = 3;
    public TimeSpan InitialBackoff { get; set; } = TimeSpan.FromMilliseconds(200);
    public TimeSpan MaxBackoff { get; set; } = TimeSpan.FromSeconds(2);
    public bool AcceptAnyServerCertificate { get; set; }
}
