using System.Runtime.CompilerServices;

// Lets CustomerManager.UnitTests set Customer.RowVersion directly (internal
// setter) to construct a stale-RowVersion scenario for the optimistic-
// concurrency test, without opening that setter up to Application/WebApi code.
[assembly: InternalsVisibleTo("CustomerManager.UnitTests")]
