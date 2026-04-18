namespace IfcEnvelopeMapper.Core.Loading;

public interface IElementFilter
{
    bool Include(string ifcType);
}
