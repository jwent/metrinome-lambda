using Microsoft.EntityFrameworkCore;

public class OnTrackDBContext : DbContext {
	private static OnTrackDBContext? _ctx = null;
	public static OnTrackDBContext ctx {
		get {
			if (_ctx == null)
				_ctx = new OnTrackDBContext();
			return _ctx;
		}
	}

	public DbSet<Blog> Blogs { get; set; } = null!;
	public DbSet<Post> Posts { get; set; } = null!;
	public DbSet<Comment> Comments { get; set; } = null!;
	public DbSet<User> Users { get; set; } = null!;

	protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
		// => optionsBuilder.UseNpgsql("Host=ontrackdb-globaldb-us-east-1.cluster-citzbss5x6lu.us-east-1.rds.amazonaws.com;Database=postgres;Username=aura;Password=xSunCwRAlX");
		=> optionsBuilder.UseNpgsql("Host=localhost;Database=docker;Username=docker;Password=docker");
}

public class Blog {
	public int? BlogId { get; set; }
	public string? Url { get; set; }

	public List<Post>? Posts { get; set; }
}

public class Post {
	public int? PostId { get; set; }
	public string? Title { get; set; }
	public string? Content { get; set; }

	public int? BlogId { get; set; }
	public Blog? Blog { get; set; }
}

public class Comment {
	public int? CommentId { get; set; }
	public string? Text { get; set; }
}

public class User {
	public Guid? Id { get; set; }
	public string? Username { get; set; }
	public string? Password { get; set; }
}
