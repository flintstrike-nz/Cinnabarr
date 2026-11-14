using System.Collections.Generic;
using System.Linq;
using NzbDrone.Core.Datastore;
using NzbDrone.Core.Messaging.Events;

namespace NzbDrone.Core.Doctor
{
    public interface IDoctorFlagRepository : IBasicRepository<DoctorFlag>
    {
        List<DoctorFlag> GetByItem(DoctorMediaType mediaType, int itemId);
    }

    public class DoctorFlagRepository : BasicRepository<DoctorFlag>, IDoctorFlagRepository
    {
        public DoctorFlagRepository(IMainDatabase database, IEventAggregator eventAggregator)
            : base(database, eventAggregator)
        {
        }

        public List<DoctorFlag> GetByItem(DoctorMediaType mediaType, int itemId)
        {
            return Query(f => f.MediaType == mediaType && f.ItemId == itemId).ToList();
        }
    }
}
