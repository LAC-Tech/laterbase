### Overview

A fast distributed event store, designed for high write availability even under network partition.

### Target Users

Users in industries where the domain is naturally eventful. (I'm primarily thinking of supply chain & logistics, but I'm sure there's others). Probably smaller outfits where the clumsiness of traditional ERPs is failing them. Logistics is an even more specific target, as they record more info "in the field" where network resiliency matters.

### Business Objectives:

- Improve supply chain and logistics operational efficiency.
- Function during network outages and in the field.
- Optimize performance for event stream processing.
- Support multi-platform usage:

### Key Features:

- Backdating
- Multi region deployment without any loss of write availability
- Retroactive event writing for integration of existing and third-party data.
- User-defined schemas and aggregation functions.
- Focus on event sourcing and syncing.
