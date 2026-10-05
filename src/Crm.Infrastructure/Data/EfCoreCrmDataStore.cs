using Crm.Application.Abstractions;
using Crm.Domain.Common;
using Microsoft.EntityFrameworkCore;

namespace Crm.Infrastructure.Data;

/// <summary>
/// EF Core adapter for <see cref="ICrmDataStore"/>. Tables are loaded lazily on first access by the
/// use case, so a request only reads the tables it touches. New entities appended to a loaded list are
/// inserted, tracked entities are updated through change tracking, and removals are honored for
/// aggregate children that the domain deletes (quote lines, role permission grants).
/// </summary>
public sealed class EfCoreCrmDataStore(CrmDbContext db) : ICrmDataStore
{
    public TResult Read<TResult>(Func<CrmDataSet, TResult> query) =>
        query(new CrmDataSet(new LazySource(db, tracking: false)));

    private const int MaxCodeConflictAttempts = 3;

    public TResult Write<TResult>(Func<CrmDataSet, TResult> command)
    {
        for (var attempt = 1; ; attempt++)
        {
            try { return WriteOnce(command); }
            catch (DbUpdateException exception) when (attempt < MaxCodeConflictAttempts && IsRecordCodeConflict(exception))
            {
                // Two writers picked the same "next" record code. The transaction rolled back, so discard the
                // tracked state and run the command again against fresh data; it will pick the following code.
                db.ChangeTracker.Clear();
            }
            catch (DbUpdateException exception) when (IsRecordCodeConflict(exception))
            {
                throw new Crm.Application.Contracts.SelfServiceConflictException("ثبت هم‌زمان رکوردهای متعدد؛ لطفاً دوباره تلاش کنید.");
            }
        }
    }

    private TResult WriteOnce<TResult>(Func<CrmDataSet, TResult> command)
    {
        var source = new LazySource(db, tracking: true);
        var data = new CrmDataSet(source);
        var result = command(data);
        source.ApplyPendingChanges(data);
        try
        {
            db.SaveChanges();
            return result;
        }
        catch (DbUpdateException exception) when (IsRecordCodeConflict(exception))
        {
            throw; // handled by the retry loop in Write
        }
        catch (DbUpdateConcurrencyException exception)
        {
            throw new InvalidOperationException("رکورد توسط درخواست دیگری تغییر کرده است؛ داده‌ها را تازه‌سازی و دوباره تلاش کنید.", exception);
        }
        catch (DbUpdateException exception) when (exception.InnerException is Microsoft.Data.SqlClient.SqlException sql && sql.Number is 2601 or 2627)
        {
            throw new Crm.Application.Contracts.SelfServiceConflictException("عملیات هم‌زمان یا شناسهٔ تکراری است؛ وضعیت ذخیره‌شده را تازه‌سازی کنید.");
        }
    }

    /// <summary>Unique-index violation on a human-readable code column (all such indexes end in "_Code" or "_Code_*").</summary>
    internal static bool IsRecordCodeConflict(DbUpdateException exception) =>
        exception is not DbUpdateConcurrencyException &&
        exception.InnerException is Microsoft.Data.SqlClient.SqlException { Number: 2601 or 2627 } sql &&
        (sql.Message.Contains("_Code'", StringComparison.Ordinal) || sql.Message.Contains("_Code_", StringComparison.Ordinal));

    private sealed class LazySource(CrmDbContext db, bool tracking) : ICrmDataSetSource
    {
        // Types whose rows the domain removes from the list; everything else is append/update only.
        private static readonly HashSet<Type> Removable = [typeof(Crm.Domain.Commercial.QuoteLine), typeof(Crm.Domain.Identity.RolePermissionGrant), typeof(Crm.Domain.Customers.CustomerLogo),
            typeof(Crm.Domain.Accounts.DocumentLink), typeof(Crm.Domain.Accounts.CampaignMember), typeof(Crm.Domain.Accounts.TargetListMember),
            typeof(Crm.Domain.Accounts.ActivityParticipant), typeof(Crm.Domain.FollowUps.FollowUpTemplateStage), typeof(Crm.Domain.FollowUps.FollowUpQueueMember)];
        private readonly List<Action<CrmDataSet>> _pending = [];

        public List<T> Load<T>() where T : class
        {
            var set = db.Set<T>();
            var rows = tracking ? set.ToList() : set.AsNoTracking().ToList();
            if (tracking) RegisterChangeTracking(set, rows);
            return rows;
        }

        public List<T> Find<T>(System.Linq.Expressions.Expression<Func<T, bool>> predicate) where T : class
        {
            var set = db.Set<T>();
            if (!tracking) return set.AsNoTracking().Where(predicate).ToList();
            var rows = set.Where(predicate).ToList();
            // Rows appended earlier in this command are not in the database yet; include them so lookups stay consistent.
            var matches = predicate.Compile();
            rows.AddRange(set.Local.Where(x => db.Entry(x).State == EntityState.Added && matches(x) && !rows.Contains(x)));
            return rows;
        }

        public void Append<T>(T entity) where T : class
        {
            if (!tracking) throw new InvalidOperationException("Append is only valid inside a write.");
            db.Set<T>().Add(entity);
        }

        public void ApplyPendingChanges(CrmDataSet data)
        {
            foreach (var apply in _pending) apply(data);
        }

        private void RegisterChangeTracking<T>(DbSet<T> set, List<T> rows) where T : class
        {
            if (!typeof(Entity).IsAssignableFrom(typeof(T))) return;
            var known = rows.Cast<Entity>().Select(x => x.Id).ToHashSet();
            _pending.Add(_ =>
            {
                var retained = new HashSet<Guid>();
                foreach (var row in rows)
                {
                    var id = ((Entity)(object)row).Id;
                    retained.Add(id);
                    if (!known.Contains(id)) set.Add(row);
                }
                if (!Removable.Contains(typeof(T))) return;
                foreach (var removed in set.Local.Where(x => !retained.Contains(((Entity)(object)x).Id)).ToArray())
                    set.Remove(removed);
            });
        }
    }
}
