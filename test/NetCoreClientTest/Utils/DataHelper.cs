using System;
using System.Text;

namespace NetCoreClientTest.Utils
{
    public static class DataHelper
    {
        public static string GetReadableString(byte[] buffer)
        {
            var nullStart = Array.IndexOf(buffer, (byte)0);
            nullStart = (nullStart == -1) ? buffer.Length : nullStart;
            var ret = Encoding.Default.GetString(buffer, 0, nullStart);
            return ret;
        }
    }
}