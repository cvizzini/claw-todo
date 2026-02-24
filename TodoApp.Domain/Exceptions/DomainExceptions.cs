namespace TodoApp.Domain.Exceptions;

public class NotFoundException : Exception
{
    public NotFoundException(string message) : base(message) { }
}

public class ForbiddenException : Exception
{
    public ForbiddenException(string message) : base(message) { }
}

public class ConflictException : Exception
{
    public ConflictException(string message) : base(message) { }
}

public class ValidationException : Exception
{
    public IEnumerable<string> Errors { get; }

    public ValidationException(IEnumerable<string> errors) : base("Validation failed.")
        => Errors = errors;

    public ValidationException(string message) : base(message)
        => Errors = [message];
}

public class AccountLockedException : Exception
{
    public AccountLockedException(string message) : base(message) { }
}

public class TenantInactiveException : Exception
{
    public TenantInactiveException(string message) : base(message) { }
}
