using System.Data;
using Dapper;

namespace AutoPartsERP.Infrastructure.Persistence;

/// <summary>
/// Dapper's record/positional-DTO constructor matching requires an exact CLR type match
/// between each reader column and the corresponding constructor parameter. Npgsql returns
/// <see cref="DateTime"/> for Postgres <c>date</c>/<c>timestamptz</c> columns by default, which
/// does not match DTOs typed as <see cref="DateOnly"/> or <see cref="DateTimeOffset"/> - Dapper
/// then reports "no parameterless constructor or matching signature" even though the data is
/// perfectly readable. Registering handlers for these types fixes constructor-based
/// materialization without touching every DTO or every hand-written SQL projection.
/// </summary>
public static class DapperTypeHandlers
{
    public static void Register()
    {
        SqlMapper.AddTypeHandler(new DateOnlyHandler());
        SqlMapper.AddTypeHandler(typeof(DateOnly?), new NullableDateOnlyHandler());
        SqlMapper.AddTypeHandler(new DateTimeOffsetHandler());
        SqlMapper.AddTypeHandler(typeof(DateTimeOffset?), new NullableDateTimeOffsetHandler());
    }

    private sealed class DateOnlyHandler : SqlMapper.TypeHandler<DateOnly>
    {
        public override void SetValue(IDbDataParameter parameter, DateOnly value) =>
            parameter.Value = value.ToDateTime(TimeOnly.MinValue);

        public override DateOnly Parse(object value) => value switch
        {
            DateOnly d => d,
            DateTime dt => DateOnly.FromDateTime(dt),
            _ => DateOnly.Parse(value.ToString()!)
        };
    }

    private sealed class NullableDateOnlyHandler : SqlMapper.TypeHandler<DateOnly?>
    {
        public override void SetValue(IDbDataParameter parameter, DateOnly? value) =>
            parameter.Value = value is null ? DBNull.Value : value.Value.ToDateTime(TimeOnly.MinValue);

        public override DateOnly? Parse(object value) => value switch
        {
            null or DBNull => null,
            DateOnly d => d,
            DateTime dt => DateOnly.FromDateTime(dt),
            _ => DateOnly.Parse(value.ToString()!)
        };
    }

    private sealed class DateTimeOffsetHandler : SqlMapper.TypeHandler<DateTimeOffset>
    {
        public override void SetValue(IDbDataParameter parameter, DateTimeOffset value) =>
            parameter.Value = value.UtcDateTime;

        public override DateTimeOffset Parse(object value) => value switch
        {
            DateTimeOffset dto => dto,
            DateTime dt => new DateTimeOffset(DateTime.SpecifyKind(dt, DateTimeKind.Utc)),
            _ => DateTimeOffset.Parse(value.ToString()!)
        };
    }

    private sealed class NullableDateTimeOffsetHandler : SqlMapper.TypeHandler<DateTimeOffset?>
    {
        public override void SetValue(IDbDataParameter parameter, DateTimeOffset? value) =>
            parameter.Value = value is null ? DBNull.Value : (object)value.Value.UtcDateTime;

        public override DateTimeOffset? Parse(object value) => value switch
        {
            null or DBNull => null,
            DateTimeOffset dto => dto,
            DateTime dt => new DateTimeOffset(DateTime.SpecifyKind(dt, DateTimeKind.Utc)),
            _ => DateTimeOffset.Parse(value.ToString()!)
        };
    }
}
