using Microsoft.IdentityModel.Tokens;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;


public class Mutation {
	public static int counter = 1;

	public static string mute() => "shhhh!";
	public static int increment() => counter++;
	public static string? loginUser(string username, string password) {
		var user = OnTrackDBContext.ctx.Users
				.Where(u => u.Username == username && u.Password == password)
				.FirstOrDefault();

		if (user == null)
			return null;

		var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes("secretsecretsecret"));

		var claims = new List<Claim> {
			new Claim("username", user.Username),
			new Claim("role", "Admin")
		};

		var signingCredentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

		var token = new JwtSecurityToken(
			"issuer",
			"audience",
			claims,
			expires: DateTime.Now.AddDays(1),
			signingCredentials: signingCredentials
		);

		return new JwtSecurityTokenHandler().WriteToken(token);
		// return user?.Id;
	}

	public static Guid? addUser(string username, string password) {
		var user = new User { Id=Guid.NewGuid(), Username=username, Password=password };
		OnTrackDBContext.ctx.Users.Add(user);
		OnTrackDBContext.ctx.SaveChanges();

		return user.Id;
	}
}
