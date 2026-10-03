using Crm.Application.Abstractions;
using Crm.Domain.Common;
using Microsoft.EntityFrameworkCore;

namespace Crm.Infrastructure.Data;

/// <summary>
/// EF Core adapter for <see cref="ICrmDataStore"/>. Tables are loaded lazily on first access by the
/// use case, so a request only reads the tables it touches. New entities appended to a loaded list are
/// inserted, tracked entities are updated through change tracking, and removals are honored for
/// aggregate children that the domain deletes (quote lines).
/// </summary>
public sealed class EfCoreCrmDataStore(CrmDbContext db) : ICrmDataStore
{
    public TResult Read<TResult>(Func<CrmDataSet, TResult> query) =>
        query(new CrmDataSet(new LazySource(db, tracking: false)));

    public TResult Write<TResult>(Func<CrmDataSet, TResult> command)
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
        catch (DbUpdateConcurrencyException exception)
        {
            throw new InvalidOperationException("رکورد توسط درخواست دیگری تغییر کرده است؛ داده‌ها را تازه‌سازی و دوباره تلاش کنید.", exception);
        }
        catch (DbUpdateException exception) when (exception.InnerException is Microsoft.Data.SqlClient.SqlException sql && sql.Number is 2601 or 2627)
        {
            throw new Crm.Application.Contracts.SelfServiceConflictException("عملیات هم‌زمان یا شناسهٔ تکراری است؛ وضعیت ذخیره‌شده را تازه‌سازی کنید.");
        }
    }

    private sealed class LazySource(CrmDbContext db, bool tracking) : ICrmDataSetSource
    {
        // Types whose rows the domain removes from the list; everything else is append/update only.
        private static readonly HashSet<Type> Removable = [typeof(Crm.Domain.Commercial.QuoteLine)];
        private readonly List<Action<CrmDataSet>> _pending = [];

        public List<T> Load<T>() where T : class
        {
            var set = db.Set<T>();
            var rows = tracking ? set.ToList() : set.AsNoTracking().ToList();
            if (tracking) RegisterChangeTracking(set, rows);
            return rows;
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
