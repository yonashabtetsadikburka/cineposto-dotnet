Unknown weekday: the Python raises ValueError lazily, only when a candidate matches day and month; ported as ArgumentException with the same laziness
Weekday check does not change the resolved date, because the fallback loop finds the same day/month; it only decides whether the exception is raised
