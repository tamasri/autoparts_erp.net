namespace AutoPartsERP.Infrastructure.Extensions;

public static class StringExtensions
{
    public static string ToSnakeCase(this string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var builder = new StringBuilder(value.Length + 8);

        for (var i = 0; i < value.Length; i++)
        {
            var character = value[i];

            if (char.IsUpper(character))
            {
                if (i > 0 && value[i - 1] != '_' && !char.IsUpper(value[i - 1]))
                {
                    builder.Append('_');
                }

                builder.Append(char.ToLowerInvariant(character));
                continue;
            }

            if (character == '-' || character == ' ')
            {
                builder.Append('_');
                continue;
            }

            builder.Append(character);
        }

        return builder.ToString();
    }
}