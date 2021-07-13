using Nop.Web.Framework.Models;
using Nop.Web.Framework.Mvc.ModelBinding;

namespace Nop.Plugin.Payments.Assist.Models
{
    public record ConfigurationModel : BaseNopModel
    {
        [NopResourceDisplayName("Plugins.Payments.Assist.GatewayUrl")]
        public string GatewayUrl { get; set; }

        [NopResourceDisplayName("Plugins.Payments.Assist.MerchantId")]
        public string MerchantId { get; set; }

        [NopResourceDisplayName("Plugins.Payments.Assist.AuthorizeOnly")]
        public bool AuthorizeOnly { get; set; }

        [NopResourceDisplayName("Plugins.Payments.Assist.TestMode")]
        public bool TestMode { get; set; }

        [NopResourceDisplayName("Plugins.Payments.Assist.AdditionalFee")]
        public decimal AdditionalFee { get; set; }

        [NopResourceDisplayName("Plugins.Payments.Assist.Login")]
        public string Login { get; set; }

        [NopResourceDisplayName("Plugins.Payments.Assist.Password")]
        public string Password { get; set; }
    }
}