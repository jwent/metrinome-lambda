
public class Query {
    public static string hello() => "hello world!";
    public static List<User> users() => OnTrackDBContext.ctx.Users.ToList();
}
