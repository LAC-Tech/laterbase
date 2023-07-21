type ViewData = std::collections::BTreeMap<Vec<u8>, Vec<u8>>; 
type ViewFn = fn(&ViewData, &[u8]) -> ViewData;

#[derive(Clone)]
pub struct View {
	data: ViewData,
	f: ViewFn
}

impl View {
	fn new(f: ViewFn) -> Self {
		let data = ViewData::new();
		Self { data, f }
	}

	fn get(&self, id: &[u8]) -> Option<&[u8]> {
		self.data.get(id).map(|bs| bs.as_slice())
	}
}

impl View {
	fn process(&mut self, event: &[u8]) {
		let new_data = (self.f)(&self.data, event);
		self.data.extend(new_data.into_iter());
	}
}

impl std::fmt::Debug for View {
    fn fmt(&self, f: &mut std::fmt::Formatter<'_>) -> std::fmt::Result {
        f.debug_struct("View")
            .field("btree", &self.data)
            .finish()
    }
}

#[cfg(test)]
mod test {
	use super::*;

	#[derive(Debug, serde::Serialize, serde::Deserialize)]
	struct TempReading {
		location: String,
		celcius: f32
	}

	#[derive(Debug, PartialEq, serde::Serialize, serde::Deserialize)]
	struct MeanAccum {
		count: usize,
		mean: f32
	}

	impl MeanAccum {
		fn add(&self, elem: f32) -> Self {
			let count = self.count + 1;
			let mean = (self.mean + elem) / (count as f32);

			Self { count, mean }
		}

		fn new(first_elem: f32) -> Self {
			Self { count: 1, mean: first_elem }
		}
	}

	#[test]
	fn can_add_numbers() {
		let events = [
			("a", 20.0),
			("b", 12.0),
			("c", 34.0),
			("a", 13.0),
			("b", -34.0)
		].map(|(location, celcius)| TempReading { 
			location: location.to_string(),
			celcius
		});
	
		let mut running_average = View::new(|accum, event| {
			let mut result = ViewData::new();
			let event: TempReading = bincode::deserialize(event).unwrap();
		
			let id = bincode::serialize(&event.location).unwrap();
			let mean_accum: MeanAccum = accum.get(&id).map(|existing_average| {
				let mean_accum: MeanAccum = 
					bincode::deserialize(existing_average.as_slice()).unwrap();
			
				mean_accum.add(event.celcius)

			})
			.unwrap_or(MeanAccum::new(event.celcius));

			let val = bincode::serialize(&mean_accum).unwrap();

			result.insert(id, val);
			result
		});

		for e in events {
			let e = bincode::serialize(&e).unwrap();
			running_average.process(e.as_slice());
		}

		let id: Vec<u8> = bincode::serialize("a").unwrap();
		let expected: Vec<u8> = bincode::serialize(
			&MeanAccum{count: 2, mean: 16.5 as f32}).unwrap();

        let expected: Option<MeanAccum> = 
            Some(bincode::deserialize(&expected).unwrap());

		let actual: Option<MeanAccum> = running_average
			.get(&id)
			.map(|bs| bincode::deserialize(&bs).unwrap());

		assert_eq!(actual, expected);
	}
}
