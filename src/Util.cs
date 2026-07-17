using System.Text;
using System.Security.Claims;
using GraphQL;
using GraphQL.Authorization;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using System.IdentityModel.Tokens.Jwt;
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
                if (string.IsNullOrWhiteSpace(hashString))
                        return false;

                string[] segments = hashString.Split(segmentDelimiter);
                if (segments.Length < 3)
                        return false;

                byte[] hash;
                byte[] salt;
                try {
                        hash = Convert.FromHexString(segments[0]);
                        salt = Convert.FromHexString(segments[1]);
                }
                catch (FormatException) {
                        return false;
                }

                if (hash.Length == 0 || salt.Length == 0)
                        return false;

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

    public static string CreateMagicLinkUrl(
    string email,
    string magicLinkId,
    string baseUrl // e.g. https://app.ontrackanalytics.com
)
    {
        if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(magicLinkId))
            throw new ArgumentException("Email and magic link ID are required.");

        var normalizedEmail = email.Trim().ToLowerInvariant();
        var payload = $"{normalizedEmail}:{magicLinkId}";

        var hash = Convert.ToHexString(
            SHA256.HashData(Encoding.UTF8.GetBytes(payload))
        );

        // URL-encode email for safety
        var encodedEmail = Uri.EscapeDataString(normalizedEmail);

        return $"{baseUrl.TrimEnd('/')}/invite?email={encodedEmail}&token={hash}";
    }
}
