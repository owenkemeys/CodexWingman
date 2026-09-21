namespace CodexWingman.Core;

public interface IQuotaSource
{
    Task<QuotaSnapshot> ReadAsync(CancellationToken cancellationToken = default);
}
