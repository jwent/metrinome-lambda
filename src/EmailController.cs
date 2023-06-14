using System.Text;
using Amazon.SimpleEmail;
using Amazon.SimpleEmail.Model;

public class EmailController {
	public static async Task<string?> SendEmail(string toEmail, string emailSubject, string emailHtmlContent) {
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
				Source = toEmail, // "noreply@app.ontrackanalytics.com",
			});
			Console.WriteLine($"[+] SES messageId: {res.MessageId}");
			return res.MessageId;

		} catch (Exception e) {
			Console.WriteLine($"SES send error: {e.ToString()}");
			return null;
		}


	}
}


