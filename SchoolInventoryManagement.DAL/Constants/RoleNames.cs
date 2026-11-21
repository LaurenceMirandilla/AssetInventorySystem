namespace SchoolInventoryManagement.DAL.Constants
{
    public static class RoleNames
    {
        public const string Administrator = "Administrator";
        // Shown as "Property Officer"; the code still calls it AssetOfficer.
        public const string AssetOfficer = "Property Officer";
        // Buys what the Principal approved on new-item (purchase) requests.
        public const string PurchasingOfficer = "Purchasing Officer";
        public const string Teacher = "Teacher";
        public const string Staff = "Staff";
        public const string DepartmentHead = "Department Head";
        public const string Principal = "Principal";
    }
}