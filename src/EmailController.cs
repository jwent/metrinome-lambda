using GraphQL;
using Amazon.SimpleEmail;
using Amazon.SimpleEmail.Model;
using Microsoft.EntityFrameworkCore;
using Stripe;
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
                Source = "noreply@app.ontrackanalytics.com",
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
        var allTrials = await dbContext.UserOrganizations
            .Where(o => o.SubscriptionTrialStartDate.HasValue)
            .ToListAsync();

        Console.WriteLine($"[TEST] Found {allTrials.Count} org(s) with trial start dates.");
        foreach (var org in allTrials)
            Console.WriteLine($"[TEST] Org {org.Id} started {org.SubscriptionTrialStartDate}");

        // Only those expiring tomorrow
        var expiring = await dbContext.UserOrganizations
            .Where(o => o.SubscriptionTrialStartDate.HasValue &&
                        o.SubscriptionTrialStartDate.Value.AddDays(14) == tomorrow)
            .Join(dbContext.Users,
                  org => org.Id,
                  user => user.OrganizationId,
                  (org, user) => new { Org = org, User = user })
            .ToListAsync();

        if (TEST_MODE && expiring.Count == 0)
        {
            Console.WriteLine("[TEST] No trials expiring tomorrow — using first available trial for testing.");
            expiring = await dbContext.UserOrganizations
                .Where(o => o.SubscriptionTrialStartDate.HasValue)
                .Join(dbContext.Users,
                      org => org.Id,
                      user => user.OrganizationId,
                      (org, user) => new { Org = org, User = user })
                .Take(1)
                .ToListAsync();
        }

        Console.WriteLine($"[i] Running expiration logic for {expiring.Count} org(s).");

        foreach (var item in expiring)
        {
            var email = item.User.Email;
            var stripeCustomerId = item.User.StripeCustomerId;
            var expiry = item.Org.SubscriptionTrialStartDate.Value.AddDays(14);

            await SendTrialEndingEmailAsync(email, expiry);
            await CreateStripeInvoiceAsync(stripeCustomerId);
        }

        Console.WriteLine("[✅] Trial expiration check complete.");
    }

    private static async Task SendTrialEndingEmailAsync(string email, DateTime expiry)
    {
        var subject = "Your OnTrack trial is ending soon";
        var body = $@"
        <p>Hi,</p>
        <p>Your OnTrack trial ends on <b>{expiry:D}</b>.</p>
        <p>You’ll automatically be billed unless you cancel before then.</p>
        <p>Thanks,<br>The OnTrack Team</p>";

        await SendEmail(email, subject, body);
    }

    private static async Task CreateStripeInvoiceAsync(string? stripeCustomerId)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(stripeCustomerId))
            {
                Console.WriteLine($"[!] Skipping invoice: no Stripe customer ID for user {stripeCustomerId}");
                return;
            }

            StripeConfiguration.ApiKey = Environment.GetEnvironmentVariable("ONTRACK_STRIPE_SECRET_KEY");

            var invoiceService = new InvoiceService();

            var invoice = await invoiceService.CreateAsync(new InvoiceCreateOptions
            {
                Customer = stripeCustomerId,
                AutoAdvance = true,
                CollectionMethod = "send_invoice",
                DaysUntilDue = 7
            });

            Console.WriteLine($"[Stripe] Created invoice {invoice.Id} for customer {invoice.CustomerId}");
            var finalized = await invoiceService.FinalizeInvoiceAsync(invoice.Id);
            Console.WriteLine($"[Stripe] Finalized invoice status: {finalized.Status}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[!] Stripe invoice creation failed: {ex.Message}");
        }
    }
}
