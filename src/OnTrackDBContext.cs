using Microsoft.EntityFrameworkCore;
using GraphQL;
using GraphQL.Authorization;


public class OnTrackDBContext : DbContext {
	private static OnTrackDBContext? _ctx = null;
	public static OnTrackDBContext ctx {
		get => (_ctx = _ctx ?? new OnTrackDBContext());
	}

	public DbSet<User> Users { get; set; } = null!;
	public DbSet<TrackingCampaign> TrackingCampaigns { get; set; } = null!;
	public DbSet<TrackerClick> TrackerClicks { get; set; } = null!;

	protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
		=> optionsBuilder.UseNpgsql(Environment.GetEnvironmentVariable("ONTRACK_DATABASE_CONNECT_STRING"));
		// => optionsBuilder.UseNpgsql("Host=localhost;Database=docker;Username=docker;Password=docker");
}