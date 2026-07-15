using GraphQL;
using Amazon.SimpleEmail;
using Amazon.SimpleEmail.Model;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

public class EmailController
{
    public static async Task<string?> SendEmail(string toEmail, string emailSubject, string emailHtmlContent)
    {
        Console.WriteLine($"[i] Sending email to address: {toEmail}");

        try
        {
            var client = new AmazonSimpleEmailServiceClient(Amazon.RegionEndpoint.USEast1);

            var res = await client.SendEmailAsync(new SendEmailRequest
            {
                Destination = new Destination
                {
                    ToAddresses = new List<string> { toEmail }
                },
                Message = new Message
                {
                    Body = new Body
                    {
                        Html = new Content { Charset = "UTF-8", Data = emailHtmlContent }
                    },
                    Subject = new Content { Charset = "UTF-8", Data = emailSubject }
                },
                Source = "noreply@metrinome.io",
            });

            Console.WriteLine($"[+] SES messageId: {res.MessageId}");
            return res.MessageId;
        }
        catch (Exception e)
        {
            Console.WriteLine($"[!] SES send error: {e}");
            return null;
        }
    }

    // ------------------------
    // TRIAL EXPIRATION CHECK
    // ------------------------
    public static async Task CheckTrialExpirationsAsync(OnTrackDBContext dbContext)
    {
        const bool TEST_MODE = true; // 👈 set false when deploying
        var today = DateTime.UtcNow.Date;
        var tomorrow = today.AddDays(1);

        // Show everything we have first
        var allTrials = await dbContext.OrganizationCveContracts
            .Where(c => c.TierName == CveContractPricingCatalog.TrialTierName)
            .ToListAsync();

        Console.WriteLine($"[TEST] Found {allTrials.Count} trial contract(s).");
        foreach (var contract in allTrials)
            Console.WriteLine($"[TEST] Org {contract.OrganizationId} trial ends {contract.ContractEndDate}");

        // Only those expiring tomorrow
        var expiring = await dbContext.OrganizationCveContracts
            .Where(contract =>
                contract.TierName == CveContractPricingCatalog.TrialTierName &&
                contract.ContractEndDate.Date == tomorrow)
            .Join(dbContext.Users,
                  contract => contract.OrganizationId,
                  user => user.OrganizationId,
                  (contract, user) => new { Contract = contract, User = user })
            .ToListAsync();

        if (TEST_MODE && expiring.Count == 0)
        {
            Console.WriteLine("[TEST] No trials expiring tomorrow — using first available trial for testing.");
            expiring = await dbContext.OrganizationCveContracts
                .Where(contract => contract.TierName == CveContractPricingCatalog.TrialTierName)
                .Join(dbContext.Users,
                      contract => contract.OrganizationId,
                      user => user.OrganizationId,
                      (contract, user) => new { Contract = contract, User = user })
                .Take(1)
                .ToListAsync();
        }

        Console.WriteLine($"[i] Running expiration logic for {expiring.Count} org(s).");

        foreach (var item in expiring)
        {
            var email = item.User.Email;
            var expiry = item.Contract.ContractEndDate;

            await SendTrialEndingEmailAsync(email, expiry);
        }

        Console.WriteLine("[✅] Trial expiration check complete.");
    }

    private static async Task SendTrialEndingEmailAsync(string email, DateTime expiry)
    {
        var subject = "Your Metrinome trial is ending soon";
        var body = $@"
        <p>Hi,</p>
        <p>Your Metrinome trial ends on <b>{expiry:D}</b>.</p>
        <p>Update your organization subscription plan before then if you want higher limits and insights access.</p>
        <p>Thanks,<br>The Metrinome Team</p>";

        await SendEmail(email, subject, body);
    }
}
