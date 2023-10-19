using System.Text;
using System.Text.Json.Nodes;
using System.Security.Claims;
using GraphQL;
using GraphQL.Authorization;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using System.Net.Http;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Microsoft.IdentityModel.Tokens;


public class Util {

	private const int _saltSize = 16; // 128 bits
	private const int _keySize = 32; // 256 bits
	private const int _iterations = 1000;
	private static readonly HashAlgorithmName _algorithm = HashAlgorithmName.SHA256;

	private const char segmentDelimiter = '/';

	public static string SaltAndHash(string input) {
		byte[] salt = RandomNumberGenerator.GetBytes(_saltSize);
		byte[] hash = Rfc2898DeriveBytes.Pbkdf2(
			input,
			salt,
			_iterations,
			_algorithm,
			_keySize
		);
		return string.Join(
			segmentDelimiter,
			Convert.ToHexString(hash),
			Convert.ToHexString(salt),
			_iterations,
			_algorithm
		);
	}

	public static bool VerifyHash(string input, string hashString) {
		string[] segments = hashString.Split(segmentDelimiter);
		byte[] hash = Convert.FromHexString(segments[0]);
		byte[] salt = Convert.FromHexString(segments[1]);

		// dont use dynamic iterations and hash names, this is abusable.
		// only use this if you strictly whitelist the allowed values.
		// // int iterations = int.Parse(segments[2]);
		// // HashAlgorithmName algorithm = new HashAlgorithmName(segments[3]);

		byte[] inputHash = Rfc2898DeriveBytes.Pbkdf2(
			input,
			salt,
			_iterations,
			_algorithm,
			hash.Length
		);

		return CryptographicOperations.FixedTimeEquals(inputHash, hash);
	}

	public static string GetSecureRandomString(int stringLength) {
		return BitConverter.ToString(RandomNumberGenerator.GetBytes(stringLength / 2)).Replace("-", string.Empty);
	}

	public static string CompressJavascriptStub(string javascriptStub) {
		javascriptStub = Regex.Replace(javascriptStub, @"\s*\}\s*", "}");
		javascriptStub = Regex.Replace(javascriptStub, @"\s*\{\s*", "{");
		javascriptStub = Regex.Replace(javascriptStub, @"\s*\(\)\s*", "()");
		javascriptStub = Regex.Replace(javascriptStub, @"\r?\n\s+", "");

		return javascriptStub;
	}

	private static readonly HttpClient httpClient = new HttpClient();

	public static async Task<string> RequestPaymentCheckout(string plan) {
		var api_key = ValueOrDie(Environment.GetEnvironmentVariable("INTERNAL_PAYMENT_API_KEY_HASH"));
		var endpoint = ValueOrDie(Environment.GetEnvironmentVariable("ONTRACK_PAYMENT_ENDPOINT_URL"));

		Console.WriteLine("[i] sending payment request to " + endpoint);
		var response = await httpClient.PostAsync(endpoint, new StringContent(
				"{\"action\":\"/payment-lambda/start-checkout\",\"plan\":\"" + plan + "\",\"internal_api_key_hash\":\"" + api_key + "\"}",
				Encoding.UTF8, "application/json"));
		Console.WriteLine("[+] payment response: " + response);

		var responseString = await response.Content.ReadAsStringAsync();
		Console.WriteLine("[+] payment responseString: " + responseString);

		var root = ValueOrDie(JsonValue.Parse(responseString));
		Console.WriteLine("[+] result id: " + root["id"]?.ToString());

		return ValueOrDie(root["url"]).ToString();
	}

	public static T ValueOrDie<T>(T? value) {
		if (value == null)
			throw new Exception("[!] missing critical value!");
		return value;
	}

	public static SymmetricSecurityKey key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(Util.ValueOrDie(Environment.GetEnvironmentVariable("ONTRACK_JWT_SIGNING_KEY"))));
	public static string SignAuthToken(User user) {
		// create a claim
		var claims = new List<Claim> {
			new Claim("id", user.Id.ToString()),
			new Claim("role", "Customer")
		};
		// sign it
		var signingCredentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);
		var token = new JwtSecurityToken(
			Environment.GetEnvironmentVariable("ONTRACK_SITE_URL"),
			Environment.GetEnvironmentVariable("ONTRACK_SITE_URL"),
			claims,
			expires: DateTime.Now.AddDays(1),
			signingCredentials: signingCredentials
		);
		// create token
		return new JwtSecurityTokenHandler().WriteToken(token);
	}

	public static bool IsEnvironmentStage(string stage) {
		return (Environment.GetEnvironmentVariable("ONTRACK_STAGE") ?? "LOCALTEST") == stage;
	}
}
