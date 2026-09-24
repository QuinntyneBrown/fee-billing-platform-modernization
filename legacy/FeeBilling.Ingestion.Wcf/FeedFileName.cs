using System;
using System.Globalization;

namespace FeeBilling.Ingestion.Wcf
{
    /// <summary>
    /// Custodian file names look like NBIN_20260930_POS.txt: {custodian}_{yyyyMMdd}_{type}.txt
    /// Used by the archive job to file the uploads by date.
    /// </summary>
    public class FeedFileName
    {
        public string CustodianCode { get; private set; }
        public DateTime FileDate { get; private set; }
        public string FileType { get; private set; }

        public static FeedFileName Parse(string fileName)
        {
            var name = System.IO.Path.GetFileNameWithoutExtension(fileName);
            var parts = name.Split('_');
            if (parts.Length < 3)
            {
                throw new FormatException("Unexpected feed file name: " + fileName);
            }

            return new FeedFileName
            {
                CustodianCode = parts[0].ToUpper(),
                FileDate = DateTime.ParseExact(parts[1], "yyyyMMdd", CultureInfo.InvariantCulture),
                FileType = parts[2].ToUpper()
            };
        }

        public static bool TryParse(string fileName, out FeedFileName result)
        {
            try
            {
                result = Parse(fileName);
                return true;
            }
            catch (FormatException)
            {
                result = null;
                return false;
            }
        }
    }
}
