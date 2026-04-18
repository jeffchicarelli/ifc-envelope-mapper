using IfcEnvelopeMapper.Core.Surface;

namespace IfcEnvelopeMapper.Core.Grouping;

public interface IFacadeGrouper
{
    IReadOnlyList<Facade> Group(Envelope envelope);
}
