namespace CustomerManager.Application.Common;

/// <summary>Names of database constraints that Application code reacts to.
/// CustomerConfiguration pins the index name to this constant via
/// HasDatabaseName, so a rename can't silently break the 409 mapping.</summary>
public static class DatabaseConstraintNames
{
    public const string CustomerEmailUnique = "IX_Customers_Email";
}
