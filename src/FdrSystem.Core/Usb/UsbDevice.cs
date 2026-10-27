namespace FdrSystem.Core.Usb;

/// <summary>Um iPhone visto ao nível do USB, antes de qualquer emparelhamento.</summary>
public sealed record UsbDevice(
    string Path,
    ushort VendorId,
    ushort ProductId,
    string? Serial,
    string? Product,
    string? Manufacturer)
{
    public string Mode => AppleUsb.ModeOf(ProductId);
}
