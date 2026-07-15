public sealed class CveContractPricingDetails
{
	public string TierName { get; init; } = string.Empty;
	public int CommittedAnnualCves { get; init; }
	public long RatePerCveCents { get; init; }
	public long AnnualMinimumFeeCents { get; init; }
	public long AnnualContractValueCents { get; init; }
	public string Currency { get; init; } = "usd";
	public string PricingNotes { get; init; } = string.Empty;
}

public static class CveContractPricingCatalog
{
	public const string TrialTierName = "Trial";
	public const string CoreTierName = "Core";
	public const string ScaleTierName = "Scale";
	public const string EnterpriseTierName = "Enterprise";
	public const string EnterprisePlusTierName = "Enterprise+";

	public const int TrialDurationDays = 14;
	public const int TrialCommittedAnnualCves = 100;
	public const int CoreCommittedAnnualCves = 20_000;

	private const long TrialRatePerCveCents = 0;
	private const long CoreRatePerCveCents = 150;
	private const long ScaleRatePerCveCents = 100;
	private const long EnterpriseRatePerCveCents = 75;
	private const long EnterprisePlusRatePerCveCents = 60;

	private const long TrialAnnualMinimumFeeCents = 0;
	private const long CoreAnnualMinimumFeeCents = 3_000_000;
	private const long ScaleAnnualMinimumFeeCents = 10_000_000;
	private const long EnterpriseAnnualMinimumFeeCents = 22_500_000;
	private const long EnterprisePlusAnnualMinimumFeeCents = 30_000_000;

	public static CveContractPricingDetails? Resolve(OrganizationCveContract? contract)
	{
		if (contract == null)
			return null;

		var normalizedTier = (contract.TierName ?? string.Empty).Trim().ToLowerInvariant();
		var committedAnnualCves = Math.Max(contract.CommittedAnnualCVEs, 0);

		if (normalizedTier.Contains("trial"))
			return Build(contract, TrialTierName, TrialRatePerCveCents, TrialAnnualMinimumFeeCents, "Trial CVE capacity.");

		if (normalizedTier.Contains("enterprise+"))
			return Build(contract, EnterprisePlusTierName, EnterprisePlusRatePerCveCents, EnterprisePlusAnnualMinimumFeeCents, "Greater of annual minimum fee or committed-volume CVE pricing.");

		if (normalizedTier.Contains("enterprise"))
			return Build(contract, EnterpriseTierName, EnterpriseRatePerCveCents, EnterpriseAnnualMinimumFeeCents, "Annual committed CVE capacity pricing.");

		if (normalizedTier.Contains("scale"))
			return Build(contract, ScaleTierName, ScaleRatePerCveCents, ScaleAnnualMinimumFeeCents, "Annual committed CVE capacity pricing.");

		if (normalizedTier.Contains("core"))
			return Build(contract, CoreTierName, CoreRatePerCveCents, CoreAnnualMinimumFeeCents, "Annual committed CVE capacity pricing.");

		if (committedAnnualCves > 300_000)
			return Build(contract, EnterprisePlusTierName, EnterprisePlusRatePerCveCents, EnterprisePlusAnnualMinimumFeeCents, "Greater of annual minimum fee or committed-volume CVE pricing.");

		if (committedAnnualCves >= 300_000)
			return Build(contract, EnterpriseTierName, EnterpriseRatePerCveCents, EnterpriseAnnualMinimumFeeCents, "Annual committed CVE capacity pricing.");

		if (committedAnnualCves >= 100_000)
			return Build(contract, ScaleTierName, ScaleRatePerCveCents, ScaleAnnualMinimumFeeCents, "Annual committed CVE capacity pricing.");

		return Build(contract, CoreTierName, CoreRatePerCveCents, CoreAnnualMinimumFeeCents, "Annual committed CVE capacity pricing.");
	}

	public static OrganizationCveContract CreateTrialContract(Guid organizationId, DateTime startUtc)
	{
		var normalizedStart = DateTime.SpecifyKind(startUtc, DateTimeKind.Utc);
		return new OrganizationCveContract
		{
			Id = Guid.NewGuid(),
			OrganizationId = organizationId,
			TierName = TrialTierName,
			CommittedAnnualCVEs = TrialCommittedAnnualCves,
			ContractStartDate = normalizedStart,
			ContractEndDate = normalizedStart.AddDays(TrialDurationDays),
			CVEHardLimitEnabled = true,
			CreatedAt = normalizedStart,
			UpdatedAt = normalizedStart,
		};
	}

	public static bool IsTrial(OrganizationCveContract? contract)
	{
		return string.Equals(contract?.TierName?.Trim(), TrialTierName, StringComparison.OrdinalIgnoreCase);
	}

	private static CveContractPricingDetails Build(
		OrganizationCveContract contract,
		string tierName,
		long ratePerCveCents,
		long annualMinimumFeeCents,
		string pricingNotes)
	{
		var committedValueCents = (long)Math.Max(contract.CommittedAnnualCVEs, 0) * ratePerCveCents;
		return new CveContractPricingDetails
		{
			TierName = tierName,
			CommittedAnnualCves = Math.Max(contract.CommittedAnnualCVEs, 0),
			RatePerCveCents = ratePerCveCents,
			AnnualMinimumFeeCents = annualMinimumFeeCents,
			AnnualContractValueCents = Math.Max(annualMinimumFeeCents, committedValueCents),
			Currency = "usd",
			PricingNotes = pricingNotes,
		};
	}
}
