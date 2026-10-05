using System;

namespace Cashflow.Windows.Data;

public static class RetirementAge
{
    public static int YearsOn(DateTime birthDate, DateTime date)
    {
        var years = date.Year - birthDate.Year;
        return birthDate.Date.AddYears(years) > date.Date ? years - 1 : years;
    }

    public static int MonthsUntilBirthday(DateTime birthDate, int age, DateTime startDate)
    {
        var birthday = birthDate.Date.AddYears(age);
        var months = (birthday.Year - startDate.Year) * 12 + birthday.Month - startDate.Month;
        // Only monthly projection points on or before the birthday are eligible.
        return startDate.Date.AddMonths(months) > birthday ? months - 1 : months;
    }
}
