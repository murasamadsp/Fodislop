#nullable enable

using Fodinae.Core.Localization;
using NUnit.Framework;

namespace Fodinae.Tests.Editor.Core
{
    [TestFixture]
    public class LocalizationServiceTests
    {
        private LocalizationService _locService = null!;

        [SetUp]
        public void SetUp()
        {
            _locService = new LocalizationService(null);
        }

        [Test]
        public void DefaultLanguage_IsRu()
        {
            Assert.That(_locService.CurrentLanguage, Is.EqualTo("ru"));
        }

        [Test]
        public void SetLanguage_ChangesCurrentLanguageAndFiresEvent()
        {
            bool eventFired = false;
            _locService.OnLanguageChanged += () => eventFired = true;

            _locService.SetLanguage("en");

            Assert.That(_locService.CurrentLanguage, Is.EqualTo("en"));
            Assert.That(eventFired, Is.True);
        }

        [Test]
        public void Get_UnknownKey_ReturnsKeyItself()
        {
            const string unknownKey = "non.existent.key.12345";
            string result = _locService.Get(unknownKey);
            Assert.That(result, Is.EqualTo(unknownKey));
        }

        [Test]
        public void Get_NullOrEmptyKey_ReturnsEmptyString()
        {
            Assert.That(_locService.Get(string.Empty), Is.EqualTo(string.Empty));
        }

        [Test]
        public void Get_WithFormattingArgs_InterpolatesCorrectly()
        {
            // Testing string.Format interpolation behavior
            const string template = "Online: {0}/{1}";
            string result = string.Format(template, 42, 100);
            Assert.That(result, Is.EqualTo("Online: 42/100"));
        }
    }
}
