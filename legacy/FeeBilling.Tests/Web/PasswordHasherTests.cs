using FeeBilling.Web.Security;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace FeeBilling.Tests.Web
{
    [TestClass]
    public class PasswordHasherTests
    {
        [TestMethod]
        public void Hash_KnownVector()
        {
            Assert.AreEqual("QsKfSff1OdW36ePwL0/07xazRao=", PasswordHasher.Hash("Q3rT8vLm2X", "Billing2026!"));
        }

        [TestMethod]
        public void Verify_CorrectPassword_True()
        {
            Assert.IsTrue(PasswordHasher.Verify("Q3rT8vLm2X", "Billing2026!", "QsKfSff1OdW36ePwL0/07xazRao="));
        }

        [TestMethod]
        public void Verify_WrongPassword_False()
        {
            Assert.IsFalse(PasswordHasher.Verify("Q3rT8vLm2X", "billing2026!", "QsKfSff1OdW36ePwL0/07xazRao="));
        }
    }
}
