using MediQueue.Application.Abstractions;
using MediQueue.Application.Auditing;
using MediQueue.Contracts;
using MediQueue.Domain.Auditing;
using NSubstitute;

namespace MediQueue.Application.Tests.Auditing;

/// <summary>
/// The one runtime security branch in the system, with every collaborator
/// substituted.
/// </summary>
/// <remarks>
/// These prove the rule in isolation. They are not sufficient on their own:
/// mapping into a DTO and asserting on the result cannot see a value that leaks
/// past the serialiser, which is why the integration suite asserts on raw JSON
/// as well.
/// </remarks>
public class AuditQueryServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 6, 9, 0, 0, TimeSpan.Zero);

    private static readonly Guid PatientId = Guid.CreateVersion7(Now);

    private readonly IAuditRepository _audit = Substitute.For<IAuditRepository>();
    private readonly ICurrentUser _currentUser = Substitute.For<ICurrentUser>();

    private AuditQueryService Service => new(_audit, _currentUser);

    /// <summary>An entry whose diagnosis is sensitive and whose status is not.</summary>
    private static AuditEntry AnEntryWithBothKindsOfField()
    {
        var entry = AuditEntry.For(
            AuditAction.Update, "Visit", Guid.CreateVersion7(Now), Guid.CreateVersion7(Now), PatientId, Now);

        entry.Add(AuditFieldChange.Record("Diagnosis", null, "Migrén", isSensitive: true, Now));
        entry.Add(AuditFieldChange.Record("Status", "Waiting", "InTreatment", isSensitive: false, Now));

        return entry;
    }

    private void SignedInAs(UserRole role)
    {
        _currentUser.Role.Returns(role);
        _currentUser.IsAuthenticated.Returns(true);
    }

    private void TheLogContains(params AuditEntry[] entries) =>
        _audit.QueryAsync(Arg.Any<AuditQuery>(), Arg.Any<CancellationToken>())
            .Returns(new AuditPage(entries, entries.Length));

    [Fact]
    public async Task An_assistant_never_receives_a_sensitive_value()
    {
        SignedInAs(UserRole.Assistant);
        TheLogContains(AnEntryWithBothKindsOfField());

        var change = (await Service.QueryAsync(AuditQuery.Create(), default))
            .Items.ShouldHaveSingleItem()
            .Changes.Single(candidate => candidate.FieldName == "Diagnosis");

        change.NewValue.ShouldBe(AuditMapper.Redaction);
        change.OldValue.ShouldBe(AuditMapper.Redaction);

        // A real field rather than an inference from the value: a client must be
        // able to render "hidden" instead of three asterisks as though they were
        // the data.
        change.Redacted.ShouldBeTrue();
    }

    [Fact]
    public async Task A_doctor_receives_the_real_value()
    {
        // Clinical staff reading a patient's history is what a medical record is
        // for. The specification's role split is about assistants.
        SignedInAs(UserRole.Doctor);
        TheLogContains(AnEntryWithBothKindsOfField());

        var change = (await Service.QueryAsync(AuditQuery.Create(), default))
            .Items.ShouldHaveSingleItem()
            .Changes.Single(candidate => candidate.FieldName == "Diagnosis");

        change.NewValue.ShouldBe("Migrén");
        change.Redacted.ShouldBeFalse();
    }

    [Theory]
    [InlineData(UserRole.Assistant)]
    [InlineData(UserRole.Doctor)]
    public async Task A_field_that_is_not_sensitive_is_never_redacted_for_anybody(UserRole role)
    {
        SignedInAs(role);
        TheLogContains(AnEntryWithBothKindsOfField());

        var change = (await Service.QueryAsync(AuditQuery.Create(), default))
            .Items.ShouldHaveSingleItem()
            .Changes.Single(candidate => candidate.FieldName == "Status");

        change.OldValue.ShouldBe("Waiting");
        change.NewValue.ShouldBe("InTreatment");
        change.Redacted.ShouldBeFalse();
    }

    [Fact]
    public async Task An_assistant_still_sees_that_the_diagnosis_changed_and_who_changed_it()
    {
        // Redaction hides the value, not the event. An assistant is entitled to
        // know a doctor recorded something at 09:41 — that is the audit trail
        // doing its job.
        SignedInAs(UserRole.Assistant);

        var entry = AnEntryWithBothKindsOfField();
        TheLogContains(entry);

        var mapped = (await Service.QueryAsync(AuditQuery.Create(), default)).Items.ShouldHaveSingleItem();

        mapped.UserId.ShouldBe(entry.UserId);
        mapped.OccurredAt.ShouldBe(entry.OccurredAt);
        mapped.Changes.Select(change => change.FieldName).ShouldContain("Diagnosis");
    }

    [Fact]
    public async Task An_unknown_role_is_treated_as_not_entitled()
    {
        // Fail closed. The rule is "only a doctor may", not "an assistant may
        // not", so a future third role is redacted until somebody decides
        // otherwise.
        _currentUser.Role.Returns((UserRole?)null);
        TheLogContains(AnEntryWithBothKindsOfField());

        (await Service.QueryAsync(AuditQuery.Create(), default))
            .Items.ShouldHaveSingleItem()
            .Changes.Single(change => change.FieldName == "Diagnosis")
            .Redacted.ShouldBeTrue();
    }

    [Fact]
    public async Task The_page_reports_the_total_rather_than_the_size_of_the_page()
    {
        SignedInAs(UserRole.Assistant);
        _audit.QueryAsync(Arg.Any<AuditQuery>(), Arg.Any<CancellationToken>())
            .Returns(new AuditPage([AnEntryWithBothKindsOfField()], 137));

        var page = await Service.QueryAsync(AuditQuery.Create(page: 2, pageSize: 10), default);

        page.TotalCount.ShouldBe(137);
        page.Items.Count.ShouldBe(1);
        page.Page.ShouldBe(2);
        page.PageSize.ShouldBe(10);
    }

    [Theory]
    [InlineData(null, AuditQuery.DefaultPageSize)]
    [InlineData(201, AuditQuery.MaxPageSize)]
    [InlineData(100_000, AuditQuery.MaxPageSize)]
    [InlineData(0, 1)]
    [InlineData(-5, 1)]
    [InlineData(50, 50)]
    public void A_page_size_outside_the_range_is_clamped_rather_than_refused(int? asked, int expected)
    {
        // Both ends, by the same rule. Refusing one while clamping the other
        // needs two sentences to explain and buys nothing: nobody can act
        // differently on being told their page size was too large.
        AuditQuery.Create(pageSize: asked).PageSize.ShouldBe(expected);
    }

    [Theory]
    [InlineData(null, 1)]
    [InlineData(0, 1)]
    [InlineData(-3, 1)]
    [InlineData(4, 4)]
    public void A_page_below_the_first_one_is_clamped_to_the_first(int? asked, int expected)
    {
        AuditQuery.Create(page: asked).Page.ShouldBe(expected);
    }

    [Fact]
    public void The_skip_follows_from_the_clamped_page_and_size()
    {
        // Guards the arithmetic that decides which rows a page contains: an
        // off-by-one here silently drops or repeats an audit entry.
        AuditQuery.Create(page: 1, pageSize: 50).Skip.ShouldBe(0);
        AuditQuery.Create(page: 3, pageSize: 20).Skip.ShouldBe(40);
        AuditQuery.Create(page: 0, pageSize: 20).Skip.ShouldBe(0);
        AuditQuery.Create(page: 2, pageSize: 100_000).Skip.ShouldBe(AuditQuery.MaxPageSize);
    }

    [Fact]
    public async Task The_filters_reach_the_repository_unchanged()
    {
        SignedInAs(UserRole.Assistant);
        TheLogContains();

        var userId = Guid.CreateVersion7(Now);
        var query = AuditQuery.Create(PatientId, userId, Now, Now.AddHours(1));

        await Service.QueryAsync(query, default);

        await _audit.Received(1).QueryAsync(
            Arg.Is<AuditQuery>(actual =>
                actual != null
                && actual.PatientId == PatientId
                && actual.UserId == userId
                && actual.From == Now
                && actual.To == Now.AddHours(1)),
            Arg.Any<CancellationToken>());
    }
}
