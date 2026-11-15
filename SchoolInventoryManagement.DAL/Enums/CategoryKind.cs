namespace SchoolInventoryManagement.DAL.Entities.Enums
{
    // What a category groups. A category is for one or the other, chosen
    // when it is created and never changed afterwards.
    public enum CategoryKind
    {
        // Groups models; its code prefix starts every asset code.
        Asset,

        // Groups supplies. No code prefix: supplies are counted by
        // quantity and have no codes.
        Supply
    }
}
