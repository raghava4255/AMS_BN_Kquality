using MailKit;
using MailKit.Net.Imap;
using MailKit.Search;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using MimeKit;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Ams.Services
{
    public class ImapEmailService
    {
        private readonly IConfiguration _configuration;
        private readonly ILogger<ImapEmailService> _logger;

        public ImapEmailService(IConfiguration configuration, ILogger<ImapEmailService> logger)
        {
            _configuration = configuration;
            _logger = logger;
        }

        public async Task<List<IncomingEmail>> FetchUnreadEmailsAsync(CancellationToken cancellationToken)
        {
            var emails = new List<IncomingEmail>();

            var server = _configuration["EmailSettings:ImapServer"];
            var portString = _configuration["EmailSettings:ImapPort"];
            var username = _configuration["EmailSettings:ImapUsername"];
            var password = _configuration["EmailSettings:ImapPassword"];

            if (string.IsNullOrEmpty(server) || string.IsNullOrEmpty(username) || string.IsNullOrEmpty(password))
            {
                _logger.LogWarning("IMAP settings are not fully configured. Skipping email fetch.");
                return emails;
            }

            int port = int.TryParse(portString, out int p) ? p : 993;

            try
            {
                using var client = new ImapClient();
                // Connect to the server
                await client.ConnectAsync(server, port, true, cancellationToken);
                
                // Authenticate
                await client.AuthenticateAsync(username, password, cancellationToken);

                // Open the Inbox folder
                var inbox = client.Inbox;
                await inbox.OpenAsync(FolderAccess.ReadWrite, cancellationToken);

                // Search for unread emails
                var query = SearchQuery.NotSeen;
                var uids = await inbox.SearchAsync(query, cancellationToken);

                foreach (var uid in uids)
                {
                    var message = await inbox.GetMessageAsync(uid, cancellationToken);

                    var incomingEmail = new IncomingEmail
                    {
                        MessageId = message.MessageId ?? Guid.NewGuid().ToString(),
                        SenderAddress = message.From.Count > 0 ? ((MailboxAddress)message.From[0]).Address : "Unknown",
                        SenderName = message.From.Count > 0 ? ((MailboxAddress)message.From[0]).Name : "Unknown",
                        Subject = message.Subject ?? "No Subject",
                        BodyText = message.TextBody ?? string.Empty,
                        BodyHtml = message.HtmlBody ?? string.Empty,
                        ReceivedDate = message.Date.UtcDateTime,
                        IsProcessed = false,
                        CreatedAt = DateTime.UtcNow
                    };

                    emails.Add(incomingEmail);

                    // Mark as read so we don't fetch it again
                    await inbox.AddFlagsAsync(uid, MessageFlags.Seen, true, cancellationToken);
                }

                await client.DisconnectAsync(true, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error occurred while fetching emails via IMAP.");
            }

            return emails;
        }
    }
}
