
public class Mutation {
    public static int counter = 1;

    public static string mute() => "shhhh!";
    public static int increment() => counter++;

    public static Guid? addUser(string username, string password) {
        var ctx = new OnTrackDBContext();
        var user = new User { Id=Guid.NewGuid(), Username=username, Password=password };
        ctx.Users.Add(user);
        ctx.SaveChanges();

        return user.Id;
    }
}
