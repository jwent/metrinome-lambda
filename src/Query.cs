
using GraphQL;
using GraphQL.Authorization;

public class Query {
	public static string hello() => "hello world!";
	[Authorize(Policy = "AdminPolicy")]
	public static List<User> users() => OnTrackDBContext.ctx.Users.ToList();
}
