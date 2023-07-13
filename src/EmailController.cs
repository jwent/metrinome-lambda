using System.IO;
using System.Text;
using Amazon.SimpleEmail;
using Amazon.SimpleEmail.Model;

public class EmailController {
	public static async Task<string?> SendEmail(string toEmail, string emailSubject, string emailHtmlContent) {
		if ((Environment.GetEnvironmentVariable("ONTRACK_STAGE") ?? "LOCALTEST") == "LOCALTEST") {
			Console.WriteLine($"[!] MOCK email to address: {toEmail}, writing to ./mock_ses_email.txt");
			File.WriteAllText("mock_ses_email.txt", $"to:{toEmail}\nsub:{emailSubject}\n{emailHtmlContent}\n\n\n");
			return null;
		}

		Console.WriteLine($"[i] sending email to address: {toEmail}");
		try {

			var client = new AmazonSimpleEmailServiceClient(Amazon.RegionEndpoint.USEast1);
			var res = await client.SendEmailAsync(new SendEmailRequest {
				Destination = new Destination {
					ToAddresses = new List<string> { toEmail }
				},
				Message = new Message {
					Body = new Body {
						Html = new Content { Charset = "UTF-8", Data = emailHtmlContent }
					},
					Subject = new Content { Charset = "UTF-8", Data = emailSubject }
				},
				Source = "mirror12k+ontracktest@gmail.com", // "noreply@app.ontrackanalytics.com",
			});
			Console.WriteLine($"[+] SES messageId: {res.MessageId}");
			return res.MessageId;

		} catch (Exception e) {
			Console.WriteLine($"SES send error: {e.ToString()}");
			return null;
		}


	}
}


