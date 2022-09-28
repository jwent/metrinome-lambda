using Microsoft.EntityFrameworkCore;

public class OnTrackDBContext : DbContext
{
	public DbSet<Blog> Blogs { get; set; }
	public DbSet<Post> Posts { get; set; }
	public DbSet<Comment> Comments { get; set; }

	protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
		=> optionsBuilder.UseNpgsql("Host=localhost;Database=docker;Username=docker;Password=docker");
}

public class Blog
{
	public int BlogId { get; set; }
	public string Url { get; set; }

	public List<Post> Posts { get; set; }
}

public class Post
{
	public int PostId { get; set; }
	public string Title { get; set; }
	public string Content { get; set; }

	public int BlogId { get; set; }
	public Blog Blog { get; set; }
}

public class Comment
{
	public int CommentId { get; set; }
	public string Text { get; set; }
}
