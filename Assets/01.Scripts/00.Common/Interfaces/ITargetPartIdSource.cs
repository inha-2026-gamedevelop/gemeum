namespace Geumeum.Common
{
    /// <summary>
    /// Optional fixed part identity for an aim/damage adapter.  Consumers treat
    /// targets without this contract as the legacy body PartId 0
    /// </summary>
    public interface ITargetPartIdSource
    {
        byte TargetPartId { get; }
        bool IsTargetable { get; }
    }
}
