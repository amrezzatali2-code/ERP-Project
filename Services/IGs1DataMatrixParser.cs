namespace ERP.Services
{
    public interface IGs1DataMatrixParser
    {
        Gs1ScanData Parse(string rawScan);
    }
}
