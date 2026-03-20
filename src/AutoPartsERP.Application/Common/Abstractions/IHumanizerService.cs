namespace AutoPartsERP.Application.Common.Abstractions;

public interface IHumanizerService
{
    string HumanizeEnum(Enum value);
    string HumanizeDate(DateTimeOffset value);
    string HumanizeTimeSpan(TimeSpan value);
    string AmountToWords(decimal amount, string currencyName);
    string FormatPeriod(int year, int month);
}
