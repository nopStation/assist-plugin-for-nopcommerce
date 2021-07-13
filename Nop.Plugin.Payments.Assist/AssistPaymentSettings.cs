using Nop.Core.Configuration;

namespace Nop.Plugin.Payments.Assist
{
    public class AssistPaymentSettings : ISettings
    {
        public string MerchantId { get; set; }
        public string GatewayUrl { get; set; }
        public bool AuthorizeOnly { get; set; }
        public bool TestMode { get; set; }
        public decimal AdditionalFee { get; set; }
        public string Login { get; set; }
        public string Password { get; set; }
    }
}
