namespace CustomerManager.Contracts.Customers.Import;

public enum ImportRowStatus
{
    Valid,
    Invalid,
    DuplicateInFile,
    DuplicateInDatabase
}
