using Core.Interfaces;
using Infrastructure.DBContext;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Text;
using System.Threading.Tasks;

namespace Infrastructure.Repositories
{
    public class GenericRepository<T> : IGenericRepository<T> where T : class
    {
        private readonly AppDbContext _context;
        public GenericRepository(AppDbContext context)
        {
            _context = context;
        }

        public async Task AddAsync(T entity)
        {
            await _context.Set<T>().AddAsync(entity);
            await _context.SaveChangesAsync();
        }

        public async Task<int> CountAsync() => await _context.Set<T>().CountAsync();

        public async Task<int> CountAsync(Expression<Func<T, bool>> predicate)
        {
            return await _context.Set<T>().CountAsync(predicate);
        }

        public async Task DeleteAsync(int id)
        {
            var entity = await _context.Set<T>().FindAsync(id);
            if (entity == null) return;
            _context.Set<T>().Remove(entity);
            await _context.SaveChangesAsync();
        }

        public IEnumerable<T> GetAll() => _context.Set<T>().AsNoTracking().ToList();

        public IEnumerable<T> GetAll(params Expression<Func<T, bool>>[] includes) => _context.Set<T>().AsNoTracking().ToList();

        public async Task<IReadOnlyList<T>> GetAllAsync() => await _context.Set<T>().AsNoTracking().ToListAsync();

        public async Task<IEnumerable<T>> GetAllAsync(params Expression<Func<T, object>>[] includes)
        {
            var query = _context.Set<T>().AsQueryable();
            foreach (var item in includes)
            {
                query = query.Include(item);
            }
            return await query.ToListAsync();
        }

        public async Task<T> GetAsync(int id) => await _context.Set<T>().FindAsync(id);

        public async Task<T> GetAsync(T id) => await _context.Set<T>().FindAsync(id);

        public async Task<T> GetByIdAsync(int id, params Expression<Func<T, object>>[] includes)
        {
            var entityType = _context.Model.FindEntityType(typeof(T));
            var keyName = entityType.FindPrimaryKey().Properties[0].Name;
            var parameter = Expression.Parameter(typeof(T), "x");
            var property = Expression.Property(parameter, keyName);
            var constant = Expression.Constant(id);
            var equal = Expression.Equal(property, constant);
            var predicate = Expression.Lambda<Func<T, bool>>(equal, parameter);
            IQueryable<T> query = _context.Set<T>();
            foreach (var include in includes)
            {
                query = query.Include(include);
            }
            return await query.FirstOrDefaultAsync(predicate);
        }

        public async Task UpdateAsync(int id, T entity)
        {
            var entity_value = await _context.Set<T>().FindAsync(id);
            if (entity_value != null)
            {
                _context.Update(entity_value);
                await _context.SaveChangesAsync();
            }
        }
    }
}
