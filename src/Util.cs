using System.Security.Claims;
using GraphQL;
using GraphQL.Authorization;
using System.Security.Cryptography;
using System.Text.RegularExpressions;

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

	public static string CompressJavascriptStub(string javascriptStub) {
		javascriptStub = Regex.Replace(javascriptStub, @"\s*\}\s*", "}");
		javascriptStub = Regex.Replace(javascriptStub, @"\s*\{\s*", "{");
		javascriptStub = Regex.Replace(javascriptStub, @"\s*\(\)\s*", "()");
		javascriptStub = Regex.Replace(javascriptStub, @"\r?\n\s+", "");

		return javascriptStub;
	}

	public static Guid GetCurrentUserId(IResolveFieldContext context) {
		if (context.User.Identity is ClaimsIdentity identity)
			return Guid.Parse(identity.FindFirst("id").Value);
		else
			throw new Exception("id claim missing");
	}

	public static TrackingCampaign GetCampaignById(OnTrackDBContext onTrackDBContext, Guid userId, Guid id) {
		Console.WriteLine($"[+] searching campaigns by campaignId: ${id}");
		var existingCampaign = onTrackDBContext.TrackingCampaigns.FirstOrDefault(e => e.Id == id && e.ParentTracker.Owner.Id == userId);
		if (existingCampaign == null)
		    throw new Exception("campaign not found!");
		return existingCampaign;
	}
}
