using System;

namespace SchoolInventoryManagement.Web.Helpers
{
    public static class RowVersionHelper
    {
        public static string ToBase64(byte[] rowVersion) => Convert.ToBase64String(rowVersion);
        public static byte[] FromBase64(string rowVersionBase64) => Convert.FromBase64String(rowVersionBase64);
    }
}