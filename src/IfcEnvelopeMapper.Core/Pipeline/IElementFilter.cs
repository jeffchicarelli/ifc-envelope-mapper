namespace IfcEnvelopeMapper.Core.Pipeline;

public interface IElementFilter
{
    bool Include(string ifcType);
}
