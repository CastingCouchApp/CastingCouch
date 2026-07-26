using CreatorControlSuite.Core.Configuration;

namespace CreatorControlSuite.Core.Validation;

public interface ISettingsValidator
{
    ValidationReport Validate(AppSettings settings);
}
