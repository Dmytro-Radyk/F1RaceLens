from ..features import FEATURES

QUALIFYING_FEATURES = FEATURES+['qualifying_position','qualifying_known']


def attach(frame, qualifying):
    """Only qualification position enters the current race; never final grid/result."""
    lookup = {(r['season'],r['round'],r['driver']):r for r in qualifying}
    if len(lookup) != len(qualifying):
        raise ValueError('Duplicate qualifying keys')
    result = frame.copy()
    positions,known = [],[]
    for row in result.itertuples():
        match = lookup.get((row.season,row.round,row.driver))
        if match and (match['team'] != row.team or match['circuit'] != row.circuit):
            raise ValueError('Qualifying entrants/circuit differ from race input')
        if match and match.get('position_ambiguous'):
            match = None
        positions.append(match['position']/row.field_size if match else .55)
        known.append(int(match is not None))
    result['qualifying_position'] = positions
    result['qualifying_known'] = known
    return result
