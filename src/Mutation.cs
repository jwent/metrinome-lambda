
public class Mutation {
    public static int counter = 1;

    public static string mute() => "shhhh!";
    public static int increment() => counter++;
    public static Guid? loginUser(string username, string password) {
        var user = OnTrackDBContext.ctx.Users
                .Where(u => u.Username == username && u.Password == password)
                .FirstOrDefault();
        return user?.Id;
    }

    public static Guid? addUser(string username, string password) {
        var user = new User { Id=Guid.NewGuid(), Username=username, Password=password };
        OnTrackDBContext.ctx.Users.Add(user);
        OnTrackDBContext.ctx.SaveChanges();

        return user.Id;
    }
}
