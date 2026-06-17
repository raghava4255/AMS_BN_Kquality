using System.Collections.Generic;
using System.Threading.Tasks;

namespace Ams.Services
{
    public interface IEmailService
    {
        Task SendEmailAsync(string to, string subject, string bodyHtml);
        Task SendTemplatedEmailAsync(string to, string subject, string templateName, Dictionary<string, string> placeholders);
        Task RetryEmailAsync(int emailLogId);
    }
}
