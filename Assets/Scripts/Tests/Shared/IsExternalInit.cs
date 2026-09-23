// Тестовые сборки собираются без System.Runtime.CompilerServices.IsExternalInit,
// а на нём держатся record и init-сеттеры. Kern.Tests.Shared стала тестовой
// сборкой ради NUnit (замер оперативки), поэтому тип объявлен здесь.
namespace System.Runtime.CompilerServices
{
    internal static class IsExternalInit
    {
    }
}
